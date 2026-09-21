using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Route("maps")]
public sealed class MapTilesController(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<MapTilesController> logger) : ControllerBase
{
    private const string LocalTomTomStyleFile = "promap-dark.json";
    private const string LocalTomTomStyleUrl = "/styles/promap-dark.json";
    private const string TomTomHost = "api.tomtom.com";
    private const string TomTomGlyphsUrl = "https://api.tomtom.com/maps-sdk-js/glyphs/v1/" + "{fontstack}/{range}.pbf";

    private static readonly HashSet<string> Layers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "dark",
            "satellite",
            "flow",
            "incidents"
        };

    // ============================================================
    // PATH HELPERS
    // ============================================================

    private string TomTomStyleFilePath()
    {
        var webRoot =
            environment.WebRootPath;

        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot =
                Path.Combine(
                    environment.ContentRootPath,
                    "wwwroot");
        }

        return Path.Combine(
            webRoot,
            "styles",
            LocalTomTomStyleFile);
    }

    // ============================================================
    // MAP CONFIG
    // ============================================================

    [HttpGet("config")]
    public IActionResult Config()
    {
        var hasTomTomKey =
            !string.IsNullOrWhiteSpace(
                configuration["TomTom:ApiKey"]);

        var styleExists =
            System.IO.File.Exists(
                TomTomStyleFilePath());

        var customStyleEnabled =
            hasTomTomKey &&
            styleExists;


        var europePmtilesPath = 
            Path.Combine(string.IsNullOrWhiteSpace(environment.WebRootPath) ?                                     
            Path.Combine(environment.ContentRootPath, "wwwroot") : environment.WebRootPath, "maps", "europe.pmtiles");

            var europePmtilesExists = System.IO.File.Exists(europePmtilesPath);


        return Ok(new
        {
            defaultBase = customStyleEnabled ? "tomtom-custom" : "osm",
            customStyleUrl = styleExists ? LocalTomTomStyleUrl : null,
            customStyleConfigured =  customStyleEnabled,
            customStyleProvider = "TomTom",
            customStyleName = "Dark Driving",
            customStyleVersion = "Draft",
            customStyleFile = LocalTomTomStyleUrl,
            europePmtiles =
                new
                {
                    enabled = europePmtilesExists,
                    url = europePmtilesExists ? "/maps/europe" : null,  
                    file = "europe.pmtiles"
                },

            layers = new[]
            {
                new
                {
                    id = "tomtom-custom",
                    label = "TomTom Dark · Custom",
                    type = "base",
                    enabled = customStyleEnabled
                },
                new
                {
                    id = "dark",
                    label = "TomTom Dark Raster",
                    type = "base",
                    enabled = hasTomTomKey
                },
                new
                {
                    id = "osm",
                    label = "OSM Light",
                    type = "base",
                    enabled = true
                },
                new
                {
                    id = "satellite",
                    label = "Satellite",
                    type = "base",
                    enabled = true
                },
                new
                {
                    id = "flow",
                    label = "Traffic Flow",
                    type = "overlay",
                    enabled = hasTomTomKey
                },
                new
                {
                    id = "incidents",
                    label = "Traffic Incidents",
                    type = "overlay",
                    enabled = hasTomTomKey
                }
            }
        });
    }



    // ============================================================
    // LOCAL EUROPE PMTILES
    // ============================================================
    [HttpGet("europe")]
    public IActionResult EuropePmtiles()
    {
        var webRoot = environment.WebRootPath;

        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(
                environment.ContentRootPath,
                "wwwroot");
        }

        var path = Path.Combine(
            webRoot,
            "maps",
            "europe.pmtiles");

        if (!System.IO.File.Exists(path))
        {
            logger.LogError(
                "Europe PMTiles file not found: {Path}",
                path);

            return NotFound(new
            {
                code = "europe_pmtiles_not_found",
                file = "wwwroot/maps/europe.pmtiles",
                path
            });
        }

        var fileInfo = new FileInfo(path);

        if (fileInfo.Length <= 0)
        {
            logger.LogError(
                "Europe PMTiles file is empty: {Path}",
                path);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    code = "europe_pmtiles_empty",
                    path
                });
        }

        // Validate PMTiles magic bytes.
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            var magic = new byte[2];

            var read = stream.Read(
                magic,
                0,
                2);

            if (
                read != 2 ||
                magic[0] != (byte)'P' ||
                magic[1] != (byte)'M')
            {
                logger.LogError(
                    "Invalid PMTiles file: {Path}",
                    path);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        code = "europe_pmtiles_invalid_magic",
                        path
                    });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed reading PMTiles header: {Path}",
                path);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    code = "europe_pmtiles_read_error",
                    path
                });
        }

        Response.Headers["Accept-Ranges"] = "bytes";
        Response.Headers["Cache-Control"] =
            "public,max-age=3600";
        Response.Headers["X-ProMap-PMTiles"] = "enabled";

        return PhysicalFile(
            path,
            "application/vnd.pmtiles",
            enableRangeProcessing: true);
    }



    // ============================================================
    // LOCAL TOMTOM CUSTOM STYLE
    // ============================================================

    [HttpGet("tomtom/style")]
    public async Task<IActionResult> TomTomStyle(CancellationToken ct)
    {
        var stylePath = TomTomStyleFilePath();

        if (!System.IO.File.Exists(stylePath))
        {
            logger.LogError(
                "TomTom custom style file not found: {Path}",
                stylePath);

            return NotFound(
                new
                {
                    code =
                        "tomtom_local_style_not_found",

                    file =
                        LocalTomTomStyleFile,

                    path =
                        stylePath
                });
        }

        var apiKey =
            configuration["TomTom:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code =
                        "tomtom_api_key_missing"
                });
        }

        try
        {
            var json =
                await System.IO.File.ReadAllTextAsync(
                    stylePath,
                    ct);

            if (string.IsNullOrWhiteSpace(json))
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        code =
                            "tomtom_local_style_empty"
                    });
            }

            JsonNode? styleNode;

            try
            {
                styleNode =
                    JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                logger.LogError(
                    ex,
                    "Invalid local TomTom style JSON: {Path}",
                    stylePath);

                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        code =
                            "tomtom_local_style_invalid_json"
                    });
            }

            if (styleNode is not JsonObject style)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        code =
                            "tomtom_local_style_invalid_root"
                    });
            }

            // ----------------------------------------------------
            // MAPLIBRE VERSION
            // ----------------------------------------------------

            var version =
                style["version"]?.GetValue<int>() ?? 0;

            if (version != 8)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        code =
                            "tomtom_local_style_not_maplibre_v8",

                        version
                    });
            }

            // ----------------------------------------------------
            // LAYERS
            // ----------------------------------------------------

            if (
                style["layers"] is not JsonArray
            )
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        code =
                            "tomtom_local_style_layers_missing"
                    });
            }

            // ----------------------------------------------------
            // GLYPHS
            // ----------------------------------------------------

            /*
             * CRITICAL FIX
             *
             * Original exported style može sadržati
             * neispravan glyphs URL.
             *
             * MapLibre zahteva:
             *
             * {fontstack}
             * {range}
             *
             * Ne koristimo proxy URL ovde.
             *
             * MapLibre prvo proširuje template.
             * Tek nakon toga transformRequest()
             * u navigation.js prebacuje konkretan
             * zahtev na /api/map/tomtom-proxy.
             */
            style["glyphs"] =
                TomTomGlyphsUrl;

            // ----------------------------------------------------
            // SERIALIZE
            // ----------------------------------------------------

            var output =
                style.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });

            Response.ContentType =
                "application/json; charset=utf-8";

            /*
             * Tokom razvoja želimo da uvek dobijemo
             * najnoviji JSON iz wwwroot.
             */
            Response.Headers.CacheControl =
                "no-store";

            return Content(
                output,
                "application/json");
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Local TomTom custom style loading failed.");

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    code =
                        "tomtom_local_style_read_error",

                    message =
                        ex.Message
                });
        }
    }

    // ============================================================
    // TOMTOM RESOURCE PROXY
    // ============================================================

    [HttpGet("tomtom-proxy")]
    public async Task<IActionResult> TomTomProxy(
        [FromQuery] string url,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest(
                new
                {
                    code =
                        "missing_url"
                });
        }

        if (
            !Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var targetUri)
        )
        {
            return BadRequest(
                new
                {
                    code =
                        "invalid_url"
                });
        }

        // --------------------------------------------------------
        // SECURITY
        // --------------------------------------------------------

        if (
            !string.Equals(
                targetUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return BadRequest(
                new
                {
                    code =
                        "scheme_not_allowed"
                });
        }

        if (
            !string.Equals(
                targetUri.Host,
                TomTomHost,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return BadRequest(
                new
                {
                    code =
                        "host_not_allowed"
                });
        }

        var apiKey =
            configuration["TomTom:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code =
                        "tomtom_api_key_missing"
                });
        }

        /*
         * Browser ne sme poslati TomTom API key.
         *
         * Ako ga style ipak ima u query stringu,
         * ovde ga uklanjamo.
         */
        var cleanUri =
            RemoveTomTomApiKey(
                targetUri);

        try
        {
            var client =
                httpClientFactory
                    .CreateClient(
                        "MapTiles");

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    cleanUri);

            // ----------------------------------------------------
            // SERVER-SIDE TOMTOM AUTH
            // ----------------------------------------------------

            request.Headers.TryAddWithoutValidation(
                "TomTom-Api-Key",
                apiKey);

            request.Headers.TryAddWithoutValidation(
                "Accept",
                "*/*");

            using var response =
                await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                var upstreamBody =
                    await response.Content
                        .ReadAsStringAsync(ct);

                logger.LogWarning(
                    "TomTom proxy resource failed. " +
                    "HTTP {StatusCode}. " +
                    "URL {Url}. " +
                    "Body {Body}",
                    (int)response.StatusCode,
                    cleanUri,
                    upstreamBody);

                return StatusCode(
                    (int)response.StatusCode,
                    new
                    {
                        code =
                            "tomtom_upstream_error",

                        status =
                            (int)response.StatusCode
                    });
            }

            var contentType =
                response.Content.Headers
                    .ContentType?
                    .MediaType;

            if (string.IsNullOrWhiteSpace(
                    contentType))
            {
                contentType =
                    GuessContentType(
                        cleanUri);
            }

            var bytes =
                await response.Content
                    .ReadAsByteArrayAsync(ct);

            if (bytes.Length == 0)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        code =
                            "tomtom_empty_resource"
                    });
            }

            Response.Headers.CacheControl =
                "public,max-age=300";

            return File(
                bytes,
                contentType);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "TomTom resource proxy failed for {Url}",
                cleanUri);

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    code =
                        "tomtom_resource_proxy_failed",

                    message =
                        ex.Message
                });
        }
    }

    // ============================================================
    // RASTER TILE ENDPOINT
    // ============================================================

    [HttpGet("tiles/{layer}/{zoom:int}/{x:int}/{y:int}.png")]
    public async Task<IActionResult> Tile(
        string layer,
        int zoom,
        int x,
        int y,
        CancellationToken ct)
    {
        layer =
            layer.ToLowerInvariant();

        if (!Layers.Contains(layer))
        {
            return NotFound();
        }

        if (zoom < 0 || zoom > 22)
        {
            return BadRequest(
                "Invalid zoom.");
        }

        var tileCount =
            1L << zoom;

        if (
            y < 0 ||
            y >= tileCount
        )
        {
            return BadRequest(
                "Invalid tile Y coordinate.");
        }

        x =
            NormalizeX(
                x,
                checked((int)tileCount));

        var apiKey =
            configuration[
                "TomTom:ApiKey"];

        var usesTomTom =
            layer == "dark" ||
            layer == "flow" ||
            layer == "incidents";

        if (
            usesTomTom &&
            string.IsNullOrWhiteSpace(
                apiKey)
        )
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code =
                        "map_provider_not_configured",

                    layer
                });
        }

        var cacheKey =
            $"map:{layer}:{zoom}:{x}:{y}";

        if (
            cache.TryGetValue(
                cacheKey,
                out MapTile? cached) &&
            cached is not null
        )
        {
            Response.Headers.CacheControl =
                $"public,max-age={GetCacheSeconds(layer)}";

            return File(
                cached.Bytes,
                cached.ContentType);
        }

        var url =
            BuildRasterUrl(
                layer,
                zoom,
                x,
                y);

        if (url is null)
        {
            return NotFound();
        }

        try
        {
            var client =
                httpClientFactory
                    .CreateClient(
                        "MapTiles");

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

            if (
                usesTomTom &&
                !string.IsNullOrWhiteSpace(
                    apiKey)
            )
            {
                request.Headers.TryAddWithoutValidation(
                    "TomTom-Api-Key",
                    apiKey);
            }

            request.Headers.TryAddWithoutValidation(
                "Accept",
                "image/png,image/*;q=0.9,*/*;q=0.8");

            using var response =
                await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Map provider request failed: " +
                    "{Layer} {Zoom}/{X}/{Y}, HTTP {StatusCode}",
                    layer,
                    zoom,
                    x,
                    y,
                    (int)response.StatusCode);

                return StatusCode(
                    (int)response.StatusCode);
            }

            var contentType =
                response.Content.Headers
                    .ContentType?
                    .MediaType;

            if (
                string.IsNullOrWhiteSpace(contentType) ||
                !contentType.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway);
            }

            var bytes =
                await response.Content
                    .ReadAsByteArrayAsync(ct);

            if (bytes.Length == 0)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway);
            }

            var tile =
                new MapTile(
                    bytes,
                    contentType);

            var cacheSeconds =
                GetCacheSeconds(
                    layer);

            cache.Set(
                cacheKey,
                tile,
                TimeSpan.FromSeconds(
                    cacheSeconds));

            Response.Headers.CacheControl =
                $"public,max-age={cacheSeconds}";

            return File(
                bytes,
                contentType);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Map tile proxy failed for " +
                "{Layer} {Zoom}/{X}/{Y}",
                layer,
                zoom,
                x,
                y);

            return StatusCode(
                StatusCodes.Status502BadGateway);
        }
    }

    // ============================================================
    // RASTER URLS
    // ============================================================

    private string? BuildRasterUrl(        string layer,        int zoom,        int x,        int y)
    {
        var apiKey =
            configuration[
                "TomTom:ApiKey"];

        if (
            layer != "satellite" &&
            string.IsNullOrWhiteSpace(
                apiKey)
        )
        {
            return null;
        }

        var trafficStyle =
            configuration[
                "TomTom:TrafficStyle"];

        trafficStyle =
            string.Equals(
                trafficStyle,
                "light",
                StringComparison.OrdinalIgnoreCase)
                ? "light"
                : "dark";

        var escapedKey =
            Uri.EscapeDataString(
                apiKey ?? string.Empty);

        return layer switch
        {
            "flow" =>
                "https://api.tomtom.com/maps/orbis/" +
                "traffic/flow/raster/tile/" +
                $"{zoom}/{x}/{y}" +
                $"?apiVersion=2" +
                $"&style={trafficStyle}" +
                $"&key={escapedKey}",

            "incidents" =>
                "https://api.tomtom.com/maps/orbis/" +
                "traffic/incidents/raster/tile/" +
                $"{zoom}/{x}/{y}" +
                $"?apiVersion=2" +
                $"&style={trafficStyle}" +
                $"&key={escapedKey}",

            "dark" =>
                "https://api.tomtom.com/map/1/tile/" +
                "basic/night/" +
                $"{zoom}/{x}/{y}.png" +
                $"?key={escapedKey}",

            "satellite" =>
                "https://server.arcgisonline.com/" +
                "ArcGIS/rest/services/" +
                "World_Imagery/MapServer/tile/" +
                $"{zoom}/{y}/{x}",

            _ =>
                null
        };
    }

    // ============================================================
    // TOMTOM URL HELPERS
    // ============================================================

    private static Uri RemoveTomTomApiKey(        Uri uri)
    {
        var result =
            RemoveQueryParameter(
                uri,
                "key");

        result =
            RemoveQueryParameter(
                result,
                "apiKey");

        return result;
    }

    private static Uri RemoveQueryParameter(        Uri uri,        string parameterName)
    {
        var builder =
            new UriBuilder(uri);

        var query =
            builder.Query.TrimStart('?');

        if (string.IsNullOrWhiteSpace(query))
        {
            return uri;
        }

        var parts =
            query.Split(
                '&',
                StringSplitOptions.RemoveEmptyEntries);

        var remaining =
            new List<string>();

        foreach (
            var part
            in parts)
        {
            var separator =
                part.IndexOf('=');

            var name =
                separator >= 0
                    ? part[..separator]
                    : part;

            if (
                string.Equals(
                    Uri.UnescapeDataString(name),
                    parameterName,
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            remaining.Add(
                part);
        }

        builder.Query =
            string.Join(
                "&",
                remaining);

        return builder.Uri;
    }

    // ============================================================
    // CONTENT TYPE
    // ============================================================

    private static string GuessContentType(Uri uri)
    {
        var path =
            uri.AbsolutePath
                .ToLowerInvariant();

        if (
            path.EndsWith(
                ".json",
                StringComparison.Ordinal)
        )
        {
            return "application/json";
        }

        if (
            path.EndsWith(
                ".pbf",
                StringComparison.Ordinal)
        )
        {
            return "application/x-protobuf";
        }

        if (
            path.EndsWith(
                ".png",
                StringComparison.Ordinal)
        )
        {
            return "image/png";
        }

        if (
            path.EndsWith(
                ".webp",
                StringComparison.Ordinal)
        )
        {
            return "image/webp";
        }

        if (
            path.EndsWith(
                ".jpg",
                StringComparison.Ordinal) ||
            path.EndsWith(
                ".jpeg",
                StringComparison.Ordinal)
        )
        {
            return "image/jpeg";
        }

        if (
            path.EndsWith(
                ".svg",
                StringComparison.Ordinal)
        )
        {
            return "image/svg+xml";
        }

        if (
            path.EndsWith(
                ".woff",
                StringComparison.Ordinal) ||
            path.EndsWith(
                ".woff2",
                StringComparison.Ordinal)
        )
        {
            return "font/woff";
        }

        return "application/octet-stream";
    }

    // ============================================================
    // CACHE
    // ============================================================

    private static int GetCacheSeconds(string layer)
    {
        return layer switch
        {
            "flow" =>
                30,

            "incidents" =>
                15,

            "dark" =>
                3600,

            "satellite" =>
                86400,

            _ =>
                60
        };
    }

    // ============================================================
    // XYZ
    // ============================================================

    private static int NormalizeX(        int x,        int count)
    {
        var value =
            x % count;

        return value < 0
            ? value + count
            : value;
    }

    // ============================================================
    // TILE MODEL
    // ============================================================

    private sealed record MapTile(        byte[] Bytes,        string ContentType);
}