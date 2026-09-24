using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Route("api/offline-routing-bundles")]
public sealed class OfflineRoutingBundlesController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<OfflineRoutingBundleCatalogItem>> Get()
    {
        var root = Path.Combine(environment.WebRootPath ?? string.Empty, "offline-bundles");
        if (string.IsNullOrWhiteSpace(environment.WebRootPath) || !Directory.Exists(root))
        {
            return Ok(Array.Empty<OfflineRoutingBundleCatalogItem>());
        }

        var graphFiles = Directory
            .EnumerateFiles(root, "*.graphbundle", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .ToDictionary(file => Path.GetFileNameWithoutExtension(file.Name), StringComparer.OrdinalIgnoreCase);

        var pmtilesFiles = Directory
            .EnumerateFiles(root, "*.pmtiles", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .ToDictionary(file => Path.GetFileNameWithoutExtension(file.Name), StringComparer.OrdinalIgnoreCase);

        var keys = graphFiles.Keys.Intersect(pmtilesFiles.Keys, StringComparer.OrdinalIgnoreCase);

        var items = keys
            .Select(key =>
            {
                var graph = graphFiles[key];
                var pmtiles = pmtilesFiles[key];
                var country = ToCountryCode(key);
                var generatedAt = graph.LastWriteTimeUtc > pmtiles.LastWriteTimeUtc ? graph.LastWriteTimeUtc : pmtiles.LastWriteTimeUtc;

                return new OfflineRoutingBundleCatalogItem(
                    Id: key,
                    CountryCode: country,
                    DisplayName: ToDisplayName(key),
                    Version: generatedAt.ToString("yyyyMMddHHmmss"),
                    GraphFileName: graph.Name,
                    GraphDownloadUrl: $"/offline-bundles/{Uri.EscapeDataString(graph.Name)}",
                    GraphSizeBytes: graph.Length,
                    PmtilesFileName: pmtiles.Name,
                    PmtilesDownloadUrl: $"/offline-bundles/{Uri.EscapeDataString(pmtiles.Name)}",
                    PmtilesSizeBytes: pmtiles.Length,
                    ChecksumSha256: string.Empty,
                    GeneratedAtUtc: generatedAt);
            })
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(items);
    }

    private static string ToCountryCode(string key)
        => key.ToUpperInvariant() switch
        {
            "SERBIA" => "RS",
            "CROATIA" => "HR",
            "BOSNIA" => "BA",
            "MONTENEGRO" => "ME",
            "SLOVENIA" => "SI",
            "NORTH-MACEDONIA" => "MK",
            "MACEDONIA" => "MK",
            "EUROPE" => "EU",
            _ => "INT"
        };

    private static string ToDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Offline bundle";
        }

        var tokens = key
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', tokens.Select(token => char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant()));
    }
}
