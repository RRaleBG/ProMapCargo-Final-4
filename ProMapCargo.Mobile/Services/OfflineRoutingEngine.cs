using System.Text.Json;
using ProMapCargo.Mobile.Models;
using ProMapCargo.OfflineRouting;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Routing and place search on the phone, from the installed offline packages (one per
/// region: graph + places). For a route the smallest installed package that covers both
/// ends is used (e.g. Srbija before Koridor 10); one graph is kept in memory at a time.
/// Search covers all installed packages. Answers in the same shapes as the server.
/// </summary>
public sealed class OfflineRoutingEngine(OfflineRoutingBundleService bundles)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, GraphBundle.Header?> headers = [];
    private readonly Dictionary<string, PlaceIndex> placeIndexes = [];
    private OfflineRouter? router;
    private string? loadedKey;

    /// <summary>The default truck the package is built for (dashboard "Profil kamiona").</summary>
    public static readonly TruckDimensions DefaultTruck = new(4.0, 2.55, 16.5, 40);

    /// <summary>Why offline routing could not be used (old format, no covering package), or null.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>How long reading the last package took on this phone (diagnostics).</summary>
    public long LastLoadMilliseconds { get; private set; }

    private sealed record Candidate(InstalledOfflineRoutingBundle Bundle, GraphBundle.Header Header, string Key);

    private async Task<List<Candidate>> InstalledAsync(CancellationToken cancellationToken)
    {
        var manifest = await bundles.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Candidate>();
        foreach (var bundle in manifest.Bundles.Where(b => File.Exists(b.GraphLocalPath) && File.Exists(b.PlacesLocalPath)))
        {
            // A reinstalled package keeps its path, so the version is part of the key.
            var key = $"{bundle.GraphLocalPath}|{bundle.Version}";
            if (!headers.TryGetValue(key, out var header))
            {
                try
                {
                    await using var stream = File.OpenRead(bundle.GraphLocalPath);
                    header = GraphBundle.ReadHeader(stream);
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
                {
                    header = null;
                    LoadProblem = $"Offline paket „{bundle.DisplayName}“ treba ažurirati (Offline mape → Paketi za rutiranje).";
                }

                headers[key] = header;
            }

            if (header is not null)
            {
                result.Add(new Candidate(bundle, header, key));
            }
        }

        return result;
    }

    /// <summary>
    /// True when a usable package is installed; loads the one covering <paramref name="near"/>
    /// (or the first) in the background so a later route does not wait for it.
    /// </summary>
    public async Task<bool> EnsureLoadedAsync(GeoPoint? near = null, CancellationToken cancellationToken = default)
    {
        var installed = await InstalledAsync(cancellationToken).ConfigureAwait(false);
        if (installed.Count == 0)
        {
            return false;
        }

        var pick = near is null
            ? installed[0]
            : installed.Where(c => c.Header.Contains(near.Lat, near.Lon)).MinBy(c => c.Header.Area) ?? installed[0];
        return await LoadAsync(pick, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> LoadAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        if (router is not null && loadedKey == candidate.Key)
        {
            return true;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (router is null || loadedKey != candidate.Key)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // Free the previous graph first: two large graphs may not fit together.
                    router = null;
                    loadedKey = null;
                    router = await Task.Run(() =>
                    {
                        using var graphStream = new BufferedStream(File.OpenRead(candidate.Bundle.GraphLocalPath), 1 << 20);
                        return new OfflineRouter(GraphBundle.Read(graphStream));
                    }, cancellationToken).ConfigureAwait(false);
                    loadedKey = candidate.Key;
                    LoadProblem = null;
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
                {
                    // Older package format or a broken download: route online only and
                    // tell the driver to update the package instead of failing later.
                    LoadProblem = $"Offline paket „{candidate.Bundle.DisplayName}“ treba ažurirati (Offline mape → Paketi za rutiranje): {ex.Message}";
                    return false;
                }

                LastLoadMilliseconds = watch.ElapsedMilliseconds;
                System.Diagnostics.Debug.WriteLine($"Offline package {candidate.Header.Name} loaded in {LastLoadMilliseconds} ms");
            }

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RouteResponse?> RouteAsync(GeoPoint start, GeoPoint destination, CancellationToken cancellationToken = default)
    {
        LoadProblem = null;
        var installed = await InstalledAsync(cancellationToken).ConfigureAwait(false);
        if (installed.Count == 0)
        {
            return null;
        }

        // Smallest package that covers both ends; a route never crosses packages.
        var covering = installed
            .Where(c => c.Header.Contains(start.Lat, start.Lon) && c.Header.Contains(destination.Lat, destination.Lon))
            .OrderBy(c => c.Header.Area)
            .ToList();
        if (covering.Count == 0)
        {
            LoadProblem = "Nijedan instalirani offline paket ne pokriva i polazak i odredište. " +
                "Preuzmite region koji obuhvata celu rutu (Offline mape → Paketi za rutiranje).";
            return null;
        }

        foreach (var candidate in covering)
        {
            if (!await LoadAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var current = router!;
                var route = await Task.Run(() => current.Route(start.Lat, start.Lon, destination.Lat, destination.Lon, DefaultTruck), cancellationToken).ConfigureAwait(false);
                if (route is not null)
                {
                    return ToResponse(route, current.Graph.GraphVersion, candidate.Header.Name);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, GeoPoint? near, int limit, CancellationToken cancellationToken = default)
    {
        var installed = await InstalledAsync(cancellationToken).ConfigureAwait(false);
        var indexes = new List<PlaceIndex>();
        foreach (var candidate in installed)
        {
            if (!placeIndexes.TryGetValue(candidate.Key, out var index))
            {
                try
                {
                    index = await Task.Run(() =>
                    {
                        using var stream = new BufferedStream(File.OpenRead(candidate.Bundle.PlacesLocalPath), 1 << 20);
                        return PlaceIndex.Read(stream);
                    }, cancellationToken).ConfigureAwait(false);
                    placeIndexes[candidate.Key] = index;
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
                {
                    continue;
                }
            }

            indexes.Add(index);
        }

        if (indexes.Count == 0)
        {
            return [];
        }

        // Regions overlap (e.g. Serbia in two packages): the same place appears once.
        var matches = await Task.Run(() => indexes
            .SelectMany(index => index.Search(query, limit, near?.Lat, near?.Lon))
            .GroupBy(m => (m.Entry.Name, Math.Round(m.Entry.Lat, 3), Math.Round(m.Entry.Lon, 3)))
            .Select(g => g.First())
            .OrderByDescending(m => m.Score)
            .Take(limit)
            .ToList(), cancellationToken).ConfigureAwait(false);

        return matches
            .Select(match => new GeocodeResult
            {
                Lat = match.Entry.Lat,
                Lon = match.Entry.Lon,
                DisplayName = string.IsNullOrWhiteSpace(match.Entry.Context)
                    ? match.Entry.Name
                    : $"{match.Entry.Name}, {match.Entry.Context}",
                Type = match.Entry.Kind == PlaceKind.Street ? "street" : "place",
            })
            .ToList();
    }

    private static RouteResponse ToResponse(OfflineRoute route, long graphVersion, string region)
    {
        var geometry = JsonSerializer.SerializeToElement(new
        {
            type = "LineString",
            coordinates = route.Points.Select(point => new[] { point.Lon, point.Lat }),
        });

        var maneuvers = route.Maneuvers
            .Select(m => new RouteManeuver(m.Index, m.Type, m.Instruction, m.DistanceFromPreviousMeters, m.DistanceFromStartMeters, m.Latitude, m.Longitude, m.RoadName, m.RoadRef, null))
            .ToList();

        // Posted limits the truck exceeds on the route (none for the default truck: the
        // package already leaves those roads out; shown for a larger profile).
        var violations = route.Violations
            .Select((v, i) => new RestrictionViolation($"offline-{i}", v.Road, v.Type, $"Ograničenje {v.Type}: {v.Limit:0.##} na putu {v.Road}"))
            .ToList();
        var safe = violations.Count == 0;

        return new RouteResponse(
            "Ok",
            [new RouteCandidate(route.DistanceMeters, route.DurationSeconds, geometry, new RouteAnalysis(!safe, safe ? 100 : 60, violations, null))],
            0,
            safe,
            violations,
            new RouteSummary(route.DistanceMeters, route.DurationSeconds, DateTimeOffset.Now.AddSeconds(route.DurationSeconds)),
            new RouteDiagnostics($"Telefon (offline · {region})", false, route.SettledNodes, graphVersion, null, route.StartSnap, route.EndSnap, 0, []),
            maneuvers);
    }
}
