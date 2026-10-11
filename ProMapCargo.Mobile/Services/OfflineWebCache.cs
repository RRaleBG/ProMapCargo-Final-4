using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Lets the navigation WebView work without a connection (dead zones on the road).
/// Every request the page makes to the ProMap server goes through here:
/// - the page, scripts, styles, fonts and icons come from the server while online and
///   are stored on the phone; offline they are served from that copy;
/// - map tiles (*.pmtiles) are read straight from the downloaded offline map, so the
///   map does not need the network (and does not use mobile data) once installed.
/// Called on a WebView worker thread, so everything here is synchronous-safe.
/// </summary>
public sealed class OfflineWebCache
{
    private static readonly HashSet<string> CacheableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".css", ".json", ".pbf", ".png", ".svg", ".jpg", ".jpeg", ".webp", ".ico",
        ".woff", ".woff2", ".ttf", ".mvt",
    };

    // Pages the app embeds; cached by path (cache-busting query parameters ignored).
    private static readonly HashSet<string> CacheablePages = new(StringComparer.OrdinalIgnoreCase)
    {
        "/navigation",
    };

    // Map labels: Latin, Serbian Latin (Latin Extended-A), Cyrillic, punctuation.
    private static readonly string[] GlyphRanges = ["0-255", "256-511", "1024-1279", "8192-8447"];
    private static readonly string[] GlyphFonts = ["Noto Sans Regular", "Noto Sans Medium", "Noto Sans Bold"];

    private static readonly TimeSpan OfflineBackoff = TimeSpan.FromSeconds(20);

    private readonly MobileServerEndpointService endpoints;
    private readonly string cacheRoot = Path.Combine(FileSystem.AppDataDirectory, "web-cache");
    private readonly string mapsManifestPath = Path.Combine(FileSystem.AppDataDirectory, "offline-maps", "manifest.json");
    private readonly HttpClient httpClient;

    private DateTimeOffset offlineUntil = DateTimeOffset.MinValue;
    private string? localMapPath;
    private DateTimeOffset localMapCheckedAt = DateTimeOffset.MinValue;

    public OfflineWebCache(MobileServerEndpointService endpoints)
    {
        this.endpoints = endpoints;
        httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
    }

    /// <summary>True while the server recently could not be reached.</summary>
    public bool IsOffline => DateTimeOffset.UtcNow < offlineUntil;

    /// <summary>Offline map file used for the map tiles, if one is installed.</summary>
    public string? LocalMapPath
    {
        get
        {
            if (DateTimeOffset.UtcNow - localMapCheckedAt > TimeSpan.FromSeconds(30))
            {
                localMapPath = FindInstalledMap();
                localMapCheckedAt = DateTimeOffset.UtcNow;
            }

            return localMapPath;
        }
    }

    /// <summary>Another check found no server: serve the stored copy right away.</summary>
    public void MarkOffline() => offlineUntil = DateTimeOffset.UtcNow + OfflineBackoff;

    /// <summary>Forget the cached map lookup (after a map was downloaded or removed).</summary>
    public void InvalidateLocalMap() => localMapCheckedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Where the map will be looked at (truck position or route start): with several
    /// regional maps installed, the one covering this point is served. Remembered across
    /// app starts: when the page opens before GPS or the route is known, the last place is
    /// used instead of the largest map (which may not cover the truck: a black map).
    /// </summary>
    public (double Lat, double Lon)? PreferredPoint
    {
        get => preferredPoint ?? (Preferences.ContainsKey(LastLatKey)
            ? (Preferences.Get(LastLatKey, 0d), Preferences.Get(LastLonKey, 0d))
            : null);
        set
        {
            preferredPoint = value;
            if (value is { } point)
            {
                Preferences.Set(LastLatKey, point.Lat);
                Preferences.Set(LastLonKey, point.Lon);
            }
        }
    }

    /// <summary>
    /// Points the map at a new place; true when another installed map covers it, so the
    /// page must reload the map (an open PMTiles archive cannot be swapped underneath).
    /// </summary>
    public bool SelectMapFor(double lat, double lon)
    {
        PreferredPoint = (lat, lon);
        var before = localMapPath;
        var selected = FindInstalledMap();
        localMapPath = selected;
        localMapCheckedAt = DateTimeOffset.UtcNow;
        return selected is not null && !string.Equals(before, selected, StringComparison.Ordinal);
    }

    private const string LastLatKey = "map.last_lat";
    private const string LastLonKey = "map.last_lon";
    private (double Lat, double Lon)? preferredPoint;

    /// <summary>
    /// Answers a WebView request, or returns null to let the WebView load it itself
    /// (other hosts, API calls, live data).
    /// </summary>
    public WebCacheResponse? Handle(string method, Uri url, IReadOnlyDictionary<string, string> requestHeaders)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || !IsServerUrl(url))
        {
            return null;
        }

        var path = url.AbsolutePath;

        if (path.EndsWith(".pmtiles", StringComparison.OrdinalIgnoreCase))
        {
            return LocalMapPath is { } mapPath ? ServeFileRange(mapPath, requestHeaders) : null;
        }

        var isPage = CacheablePages.Contains(path.TrimEnd('/'));
        if (!isPage && !CacheableExtensions.Contains(Path.GetExtension(path)))
        {
            return null;
        }

        var key = isPage ? path.TrimEnd('/').ToLowerInvariant() : url.PathAndQuery;

        // Versioned assets (?v=hash) and glyphs never change: no need to ask the server.
        var immutable = url.Query.Contains("v=", StringComparison.Ordinal) || path.EndsWith(".pbf", StringComparison.OrdinalIgnoreCase);
        if (immutable && ReadCache(key) is { } cachedFirst)
        {
            return cachedFirst;
        }

        if (!IsOffline && FetchAndStore(url, key) is { } fresh)
        {
            return fresh;
        }

        return ReadCache(key);
    }

    /// <summary>
    /// Stores the map label fonts while online, so street and place names still show
    /// in areas first seen offline.
    /// </summary>
    public async Task PrefetchGlyphsAsync()
    {
        await Task.Run(() =>
        {
            foreach (var font in GlyphFonts)
            {
                foreach (var range in GlyphRanges)
                {
                    if (IsOffline)
                    {
                        return;
                    }

                    var url = endpoints.CreateUri($"fonts/{Uri.EscapeDataString(font)}/{range}.pbf");
                    var key = url.PathAndQuery;
                    if (!File.Exists(BodyPath(key)))
                    {
                        FetchAndStore(url, key);
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    private bool IsServerUrl(Uri url)
    {
        var server = new Uri(endpoints.CurrentApiBaseUrl);
        return string.Equals(url.Host, server.Host, StringComparison.OrdinalIgnoreCase) && url.Port == server.Port;
    }

    private WebCacheResponse? FetchAndStore(Uri url, string key)
    {
        try
        {
            using var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
            var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var mime = response.Content.Headers.ContentType?.MediaType ?? GuessMime(url.AbsolutePath);
            var charset = response.Content.Headers.ContentType?.CharSet;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                // Reachable but no such file: let the page see the real status.
                return new WebCacheResponse((int)response.StatusCode, ReasonOf(response.StatusCode), mime, charset, new Dictionary<string, string>(), new MemoryStream(body));
            }

            WriteCache(key, body, mime, charset);
            return new WebCacheResponse(200, "OK", mime, charset, new Dictionary<string, string>(), new MemoryStream(body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // No connection: stop trying for a while so the page loads from the copy quickly.
            offlineUntil = DateTimeOffset.UtcNow + OfflineBackoff;
            return null;
        }
    }

    private WebCacheResponse? ReadCache(string key)
    {
        var bodyPath = BodyPath(key);
        var metaPath = bodyPath + ".meta";
        if (!File.Exists(bodyPath))
        {
            return null;
        }

        try
        {
            var meta = File.Exists(metaPath)
                ? JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(metaPath))
                : null;
            var body = File.ReadAllBytes(bodyPath);
            return new WebCacheResponse(200, "OK", meta?.Mime ?? GuessMime(key), meta?.Charset, new Dictionary<string, string>(), new MemoryStream(body));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WriteCache(string key, byte[] body, string mime, string? charset)
    {
        try
        {
            Directory.CreateDirectory(cacheRoot);
            var bodyPath = BodyPath(key);
            var tempPath = bodyPath + ".tmp";
            File.WriteAllBytes(tempPath, body);
            File.Move(tempPath, bodyPath, overwrite: true);
            File.WriteAllText(bodyPath + ".meta", JsonSerializer.Serialize(new CacheMeta(mime, charset)));
        }
        catch (Exception)
        {
            // A failed cache write only means this file is not available offline.
        }
    }

    private string BodyPath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(cacheRoot, hash);
    }

    // pmtiles reads the archive with HTTP Range requests (header, directories, tiles).
    private static WebCacheResponse ServeFileRange(string filePath, IReadOnlyDictionary<string, string> requestHeaders)
    {
        var total = new FileInfo(filePath).Length;
        var headers = new Dictionary<string, string>
        {
            ["Accept-Ranges"] = "bytes",
            ["Access-Control-Allow-Origin"] = "*",
            ["Cache-Control"] = "no-store",
        };

        var range = requestHeaders.FirstOrDefault(pair => string.Equals(pair.Key, "Range", StringComparison.OrdinalIgnoreCase)).Value;
        // The Android WebView applies the Range start itself to an intercepted response:
        // it skips to the first byte, then reads to the end of the body. So the body is
        // the archive up to the last requested byte (RangeFilePath + RangeEnd, opened as
        // a seekable stream by the platform client). No Content-Length: the WebView sets
        // its own and a duplicate breaks the response.
        if (TryParseRange(range, total, out var start, out var end))
        {
            headers["Content-Range"] = $"bytes {start}-{end}/{total}";
            return new WebCacheResponse(206, "Partial Content", "application/octet-stream", null, headers, Stream.Null, filePath, end + 1);
        }

        return new WebCacheResponse(200, "OK", "application/octet-stream", null, headers, Stream.Null, filePath, total);
    }

    private static bool TryParseRange(string? header, long total, out long start, out long end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = header[6..].Split(',')[0].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return false;
        }

        var startText = spec[..dash];
        var endText = spec[(dash + 1)..];

        if (startText.Length == 0)
        {
            // Suffix range: the last N bytes.
            if (!long.TryParse(endText, out var suffix))
            {
                return false;
            }

            start = Math.Max(0, total - suffix);
            end = total - 1;
            return true;
        }

        if (!long.TryParse(startText, out start))
        {
            return false;
        }

        end = endText.Length == 0 || !long.TryParse(endText, out var parsedEnd) ? total - 1 : Math.Min(parsedEnd, total - 1);
        return start <= end && start < total;
    }

    private string? FindInstalledMap()
    {
        try
        {
            if (!File.Exists(mapsManifestPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(mapsManifestPath));
            if (!document.RootElement.TryGetProperty("packages", out var packages))
            {
                return null;
            }

            var paths = packages.EnumerateArray()
                .Select(package => package.TryGetProperty("localPath", out var p) ? p.GetString() : null)
                .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
                .Cast<string>()
                .ToList();

            // Only archives in the navigation (Protomaps) schema can be drawn by the
            // navigation style: the regional "-nav" cuts, or the full Europe archive.
            // The older serbia.pmtiles (OpenMapTiles schema) would show an empty map.
            var maps = paths
                .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith("-nav", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(Path.GetFileName(path), "europe.pmtiles", StringComparison.OrdinalIgnoreCase))
                .Select(path => (Path: path, Bounds: PmtilesBounds(path)))
                .ToList();
            if (maps.Count == 0)
            {
                return null;
            }

            // The smallest map covering the truck (Srbija before Koridor 10), else the largest.
            if (PreferredPoint is { } point &&
                maps.Where(m => m.Bounds is { } b && point.Lat >= b.MinLat && point.Lat <= b.MaxLat && point.Lon >= b.MinLon && point.Lon <= b.MaxLon)
                    .MinBy(m => Area(m.Bounds)) is { Path: { } covering })
            {
                return covering;
            }

            return maps.MaxBy(m => Area(m.Bounds)).Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double Area((double MinLat, double MinLon, double MaxLat, double MaxLon)? b)
        => b is { } v ? (v.MaxLat - v.MinLat) * (v.MaxLon - v.MinLon) : 0;

    /// <summary>Bounds from the PMTiles v3 header (bytes 102-117: min/max lon/lat × 1e7).</summary>
    private static (double MinLat, double MinLon, double MaxLat, double MaxLon)? PmtilesBounds(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var header = new byte[127];
            file.ReadExactly(header);
            if (header[7] != 3)
            {
                return null;
            }

            double At(int offset) => BitConverter.ToInt32(header, offset) / 1e7;
            return (At(106), At(102), At(114), At(110));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".js" => "application/javascript",
        ".css" => "text/css",
        ".json" => "application/json",
        ".pbf" or ".mvt" => "application/x-protobuf",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        _ => "text/html",
    };

    private static string ReasonOf(HttpStatusCode status)
    {
        var text = status.ToString();
        return string.IsNullOrEmpty(text) ? "Status" : text;
    }

    private sealed record CacheMeta(string Mime, string? Charset);
}

public sealed record WebCacheResponse(
    int StatusCode,
    string ReasonPhrase,
    string MimeType,
    string? Charset,
    IDictionary<string, string> Headers,
    Stream Body,
    string? RangeFilePath = null,
    long RangeEnd = 0);
