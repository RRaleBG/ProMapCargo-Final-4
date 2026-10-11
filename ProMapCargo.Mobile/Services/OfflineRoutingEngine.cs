using System.Text.Json;
using ProMapCargo.Mobile.Models;
using ProMapCargo.OfflineRouting;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Routing and place search on the phone, from the installed offline package
/// (graph + places). Loaded on first use (about a second); answers in the same
/// shapes as the server so the navigation screen does not care where a route came from.
/// </summary>
public sealed class OfflineRoutingEngine(OfflineRoutingBundleService bundles)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private OfflineRouter? router;
    private PlaceIndex? places;
    private string? loadedGraphPath;

    /// <summary>Loads the installed package if there is one; false when none is installed.</summary>
    public async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await bundles.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
        var installed = manifest.Bundles.FirstOrDefault(bundle =>
            File.Exists(bundle.GraphLocalPath) && File.Exists(bundle.PlacesLocalPath));
        if (installed is null)
        {
            return false;
        }

        if (router is not null && loadedGraphPath == installed.GraphLocalPath)
        {
            return true;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (router is null || loadedGraphPath != installed.GraphLocalPath)
            {
                (router, places) = await Task.Run(() =>
                {
                    using var graphStream = new BufferedStream(File.OpenRead(installed.GraphLocalPath), 1 << 20);
                    var graph = GraphBundle.Read(graphStream);
                    using var placesStream = new BufferedStream(File.OpenRead(installed.PlacesLocalPath), 1 << 20);
                    return (new OfflineRouter(graph), PlaceIndex.Read(placesStream));
                }, cancellationToken).ConfigureAwait(false);
                loadedGraphPath = installed.GraphLocalPath;
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
        if (!await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = router!;
            var route = await Task.Run(() => current.Route(start.Lat, start.Lon, destination.Lat, destination.Lon), cancellationToken).ConfigureAwait(false);
            return route is null ? null : ToResponse(route, current.Graph.GraphVersion);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, GeoPoint? near, int limit, CancellationToken cancellationToken = default)
    {
        if (!await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false) || places is null)
        {
            return [];
        }

        var index = places;
        var matches = await Task.Run(() => index.Search(query, limit, near?.Lat, near?.Lon), cancellationToken).ConfigureAwait(false);
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

    private static RouteResponse ToResponse(OfflineRoute route, long graphVersion)
    {
        var geometry = JsonSerializer.SerializeToElement(new
        {
            type = "LineString",
            coordinates = route.Points.Select(point => new[] { point.Lon, point.Lat }),
        });

        var maneuvers = route.Maneuvers
            .Select(m => new RouteManeuver(m.Index, m.Type, m.Instruction, m.DistanceFromPreviousMeters, m.DistanceFromStartMeters, m.Latitude, m.Longitude, m.RoadName, m.RoadRef, null))
            .ToList();

        return new RouteResponse(
            "Ok",
            [new RouteCandidate(route.DistanceMeters, route.DurationSeconds, geometry, new RouteAnalysis(false, 100, [], null))],
            0,
            true,
            [],
            new RouteSummary(route.DistanceMeters, route.DurationSeconds, DateTimeOffset.Now.AddSeconds(route.DurationSeconds)),
            new RouteDiagnostics("Telefon (offline)", false, route.SettledNodes, graphVersion, null, route.StartSnap, route.EndSnap, 0, []),
            maneuvers);
    }
}
