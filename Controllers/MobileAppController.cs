using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProMapCargo.Api.Controllers;

/// <summary>
/// The newest Android app the server offers: the phone compares the version code with its
/// own and offers the update. Files are published by Tools/release/build-release.ps1 -Publish
/// into app-releases/ (version.json + promap-cargo.apk, not in git).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/mobile/app")]
public sealed class MobileAppController(IWebHostEnvironment environment) : ControllerBase
{
    private const string ApkFileName = "promap-cargo.apk";

    private string ReleaseDirectory => Path.Combine(environment.ContentRootPath, "app-releases");

    [HttpGet("latest")]
    public async Task<ActionResult<MobileAppRelease>> Latest(CancellationToken ct)
    {
        var versionFile = Path.Combine(ReleaseDirectory, "version.json");
        var apk = new FileInfo(Path.Combine(ReleaseDirectory, ApkFileName));
        if (!System.IO.File.Exists(versionFile) || !apk.Exists)
        {
            return NotFound(new { code = "NoRelease", message = "Server nema objavljenu verziju aplikacije." });
        }

        await using var stream = System.IO.File.OpenRead(versionFile);
        var info = await JsonSerializer.DeserializeAsync<MobileAppVersionFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
        if (info is null || info.VersionCode <= 0)
        {
            return NotFound(new { code = "NoRelease", message = "Opis verzije aplikacije nije ispravan." });
        }

        return Ok(new MobileAppRelease(info.VersionCode, info.VersionName, info.Notes, info.PublishedAt, apk.Length, "api/mobile/app/apk"));
    }

    [HttpGet("apk")]
    public IActionResult Apk()
    {
        var apk = Path.Combine(ReleaseDirectory, ApkFileName);
        return System.IO.File.Exists(apk)
            ? PhysicalFile(apk, "application/vnd.android.package-archive", ApkFileName, enableRangeProcessing: true)
            : NotFound();
    }
}

public sealed record MobileAppVersionFile(int VersionCode, string? VersionName, string? Notes, DateTimeOffset? PublishedAt);

public sealed record MobileAppRelease(int VersionCode, string? VersionName, string? Notes, DateTimeOffset? PublishedAt, long SizeBytes, string DownloadPath);
