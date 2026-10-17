using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

/// <summary>Button text of a package in "Offline mape".</summary>
public static class PackageInstallState
{
    public const string Install = "Instaliraj";
    public const string Update = "Ažuriraj";
    public const string Installed = "Instalirano";
}

public sealed record MapPackageCatalogItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("lastModifiedUtc")] DateTime LastModifiedUtc,
    [property: JsonPropertyName("sha256")] string? Sha256 = null)
{
    /// <summary>Instaliraj / Ažuriraj / Instalirano (set by the app).</summary>
    [JsonIgnore]
    public string InstallState { get; init; } = PackageInstallState.Install;

    [JsonIgnore]
    public bool IsInstalled => InstallState == PackageInstallState.Installed;

    [JsonIgnore]
    public string SizeText => ByteSize.Format(SizeBytes);

    // The server file is newer than the installed copy -> update.
    public MapPackageCatalogItem WithInstallState(IEnumerable<InstalledMapPackage> installed)
    {
        var current = installed.FirstOrDefault(p => string.Equals(p.Id, Id, StringComparison.OrdinalIgnoreCase));
        return this with
        {
            InstallState = current is null ? PackageInstallState.Install
                : current.SourceLastModifiedUtc >= LastModifiedUtc ? PackageInstallState.Installed
                : PackageInstallState.Update,
        };
    }
}

public sealed record InstalledMapPackage(
    string Id,
    string CountryCode,
    string DisplayName,
    string FileName,
    string LocalPath,
    long SizeBytes,
    DateTime InstalledAtUtc,
    DateTime SourceLastModifiedUtc);

/// <summary>Sizes for the screens ("1,2 GB", "340 MB").</summary>
public static class ByteSize
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        > 0 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => "—",
    };
}

public sealed record InstalledMapManifest(
    DateTime UpdatedAtUtc,
    IReadOnlyList<InstalledMapPackage> Packages)
{
    public static InstalledMapManifest Empty { get; } = new(DateTime.UtcNow, []);
}
