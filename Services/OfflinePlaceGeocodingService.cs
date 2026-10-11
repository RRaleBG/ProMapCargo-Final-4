using ProMapCargo.OfflineRouting;
using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Services;

/// <summary>
/// Place search from the offline place indexes (wwwroot/offline-bundles/*.places), the same
/// data the phones search offline. No external geocoder: the server works without internet.
/// The indexes are merged once (regions overlap, a place is kept once) and reloaded when a
/// package is rebuilt.
/// </summary>
public sealed class OfflinePlaceGeocodingService(IWebHostEnvironment environment, ILogger<OfflinePlaceGeocodingService> logger)
    : IGeocodingService
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private PlaceIndex? index;
    private string signature = "";

    public async Task<IReadOnlyList<object>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var places = await GetIndexAsync(ct);
        return places
            .Search(query, Math.Clamp(limit, 1, 10))
            .Select(match => (object)new GeocodeResult
            {
                Lat = match.Entry.Lat,
                Lon = match.Entry.Lon,
                DisplayName = string.IsNullOrWhiteSpace(match.Entry.Context)
                    ? match.Entry.Name
                    : $"{match.Entry.Name}, {match.Entry.Context}",
                Type = match.Entry.Kind == PlaceKind.Street ? "street" : match.Entry.Kind.ToString().ToLowerInvariant(),
                Class = match.Entry.Kind switch
                {
                    PlaceKind.Street => "highway",
                    >= PlaceKind.Fuel => "poi",
                    _ => "place",
                },
            })
            .ToList();
    }

    private async Task<PlaceIndex> GetIndexAsync(CancellationToken ct)
    {
        var files = PlaceFiles();
        var current = string.Join('|', files.Select(f => $"{f.Name}:{f.LastWriteTimeUtc.Ticks}"));
        if (index is not null && current == signature)
        {
            return index;
        }

        await loadLock.WaitAsync(ct);
        try
        {
            if (index is null || current != signature)
            {
                index = await Task.Run(() => Load(files), ct);
                signature = current;
            }

            return index;
        }
        finally
        {
            loadLock.Release();
        }
    }

    private FileInfo[] PlaceFiles()
    {
        var root = Path.Combine(environment.WebRootPath ?? string.Empty, "offline-bundles");
        return Directory.Exists(root)
            ? new DirectoryInfo(root).GetFiles("*.places").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
    }

    private PlaceIndex Load(FileInfo[] files)
    {
        var seen = new HashSet<(string, double, double)>();
        var entries = new List<PlaceEntry>();
        foreach (var file in files)
        {
            try
            {
                using var stream = file.OpenRead();
                foreach (var entry in PlaceIndex.Read(stream).Entries)
                {
                    if (seen.Add((entry.Key, Math.Round(entry.Lat, 3), Math.Round(entry.Lon, 3))))
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
            {
                logger.LogWarning(ex, "Place index {File} skipped.", file.Name);
            }
        }

        logger.LogInformation("Place search loaded {Count} places from {Files} indexes.", entries.Count, files.Length);
        return new PlaceIndex(entries);
    }

    private sealed class GeocodeResult
    {
        [JsonPropertyName("lat")]
        public double Lat { get; init; }

        [JsonPropertyName("lon")]
        public double Lon { get; init; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; init; } = "";

        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("class")]
        public string Class { get; init; } = "";
    }
}
