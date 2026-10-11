using Microsoft.AspNetCore.Mvc;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Route("api/map-packages")]
public sealed class MapPackagesController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<MapPackageDto>> Get()
    {
        var mapsRoot = Path.Combine(environment.WebRootPath ?? string.Empty, "maps");
        if (string.IsNullOrWhiteSpace(environment.WebRootPath) || !Directory.Exists(mapsRoot))
        {
            return Ok(Array.Empty<MapPackageDto>());
        }

        var packages = Directory
            .EnumerateFiles(mapsRoot, "*.pmtiles", SearchOption.TopDirectoryOnly)
            .Select(filePath =>
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name;
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

                return new MapPackageDto(
                    Id: nameWithoutExtension,
                    CountryCode: GuessCountryCode(nameWithoutExtension),
                    DisplayName: BuildDisplayName(nameWithoutExtension),
                    FileName: fileName,
                    DownloadUrl: $"/maps/{Uri.EscapeDataString(fileName)}",
                    SizeBytes: fileInfo.Length,
                    LastModifiedUtc: fileInfo.LastWriteTimeUtc);
            })
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // "<name>-nav.pmtiles" is the navigation-schema (Protomaps) cut of a region; the
        // plain "<name>.pmtiles" next to it uses another schema the navigation style
        // cannot draw, so the app is offered only the navigation one.
        var navigationIds = packages
            .Where(item => item.Id.EndsWith(NavigationSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Id[..^NavigationSuffix.Length])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Ok(packages.Where(item => !navigationIds.Contains(item.Id)).ToArray());
    }

    private const string NavigationSuffix = "-nav";

    private static string RegionOf(string value)
        => value.EndsWith(NavigationSuffix, StringComparison.OrdinalIgnoreCase) ? value[..^NavigationSuffix.Length] : value;

    private static string GuessCountryCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "INT";
        }

        return RegionOf(value).ToUpperInvariant() switch
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
    }

    private static string BuildDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Map package";
        }

        var tokens = RegionOf(value)
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return value;
        }

        return string.Join(' ', tokens.Select(token => char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant()));
    }

    public sealed record MapPackageDto(
        string Id,
        string CountryCode,
        string DisplayName,
        string FileName,
        string DownloadUrl,
        long SizeBytes,
        DateTime LastModifiedUtc);
}
