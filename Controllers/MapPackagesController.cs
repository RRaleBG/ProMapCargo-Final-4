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

        return Ok(packages);
    }

    private static string GuessCountryCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "INT";
        }

        return value.ToUpperInvariant() switch
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

        var tokens = value
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
