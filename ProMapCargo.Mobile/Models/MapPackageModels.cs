using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

public sealed record MapPackageCatalogItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("lastModifiedUtc")] DateTime LastModifiedUtc);

public sealed record InstalledMapPackage(
    string Id,
    string CountryCode,
    string DisplayName,
    string FileName,
    string LocalPath,
    long SizeBytes,
    DateTime InstalledAtUtc,
    DateTime SourceLastModifiedUtc);

public sealed record InstalledMapManifest(
    DateTime UpdatedAtUtc,
    IReadOnlyList<InstalledMapPackage> Packages)
{
    public static InstalledMapManifest Empty { get; } = new(DateTime.UtcNow, []);
}
