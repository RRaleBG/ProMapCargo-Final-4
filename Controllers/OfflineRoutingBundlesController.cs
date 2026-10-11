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

        var placesFiles = Directory
            .EnumerateFiles(root, "*.places", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .ToDictionary(file => Path.GetFileNameWithoutExtension(file.Name), StringComparer.OrdinalIgnoreCase);

        var keys = graphFiles.Keys.Intersect(placesFiles.Keys, StringComparer.OrdinalIgnoreCase);

        var items = keys
            .Select(key =>
            {
                var graph = graphFiles[key];
                var places = placesFiles[key];
                var country = ToCountryCode(key);
                var generatedAt = graph.LastWriteTimeUtc > places.LastWriteTimeUtc ? graph.LastWriteTimeUtc : places.LastWriteTimeUtc;

                return new OfflineRoutingBundleCatalogItem(
                    Id: key,
                    CountryCode: country,
                    DisplayName: PackageTitle(graph.FullName) ?? ToDisplayName(key),
                    Version: generatedAt.ToString("yyyyMMddHHmmss"),
                    GraphFileName: graph.Name,
                    GraphDownloadUrl: $"/offline-bundles/{Uri.EscapeDataString(graph.Name)}",
                    GraphSizeBytes: graph.Length,
                    PlacesFileName: places.Name,
                    PlacesDownloadUrl: $"/offline-bundles/{Uri.EscapeDataString(places.Name)}",
                    PlacesSizeBytes: places.Length,
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

    /// <summary>
    /// Region title stored in a package header (format v3: magic, version, graph
    /// version, build time, then the name), e.g. "Istočna Evropa"; null for older files.
    /// </summary>
    public static string? PackageTitle(string graphBundlePath)
    {
        try
        {
            using var reader = new BinaryReader(System.IO.File.OpenRead(graphBundlePath), System.Text.Encoding.UTF8);
            if (!reader.ReadBytes(4).AsSpan().SequenceEqual("PMGB"u8) || reader.ReadInt32() < 3)
            {
                return null;
            }

            reader.ReadInt64();
            reader.ReadInt64();
            var name = reader.ReadString();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

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
