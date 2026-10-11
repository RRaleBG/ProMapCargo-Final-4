using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

public sealed record OfflineRoutingBundleCatalogItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("graphFileName")] string GraphFileName,
    [property: JsonPropertyName("graphDownloadUrl")] string GraphDownloadUrl,
    [property: JsonPropertyName("graphSizeBytes")] long GraphSizeBytes,
    [property: JsonPropertyName("placesFileName")] string PlacesFileName,
    [property: JsonPropertyName("placesDownloadUrl")] string PlacesDownloadUrl,
    [property: JsonPropertyName("placesSizeBytes")] long PlacesSizeBytes,
    [property: JsonPropertyName("checksumSha256")] string ChecksumSha256,
    [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc)
{
    /// <summary>Button text in "Offline mape": Instaliraj / Ažuriraj / Instalirano (set by the app).</summary>
    [JsonIgnore]
    public string InstallState { get; init; } = PackageInstallState.Install;

    public OfflineRoutingBundleCatalogItem WithInstallState(IEnumerable<InstalledOfflineRoutingBundle> installed)
    {
        var current = installed.FirstOrDefault(b => string.Equals(b.Id, Id, StringComparison.OrdinalIgnoreCase));
        return this with
        {
            InstallState = current is null ? PackageInstallState.Install
                : current.Version == Version ? PackageInstallState.Installed
                : PackageInstallState.Update,
        };
    }
}

public sealed record InstalledOfflineRoutingBundle(
    string Id,
    string CountryCode,
    string DisplayName,
    string Version,
    string GraphLocalPath,
    string PlacesLocalPath,
    string ChecksumSha256,
    DateTime InstalledAtUtc);

public sealed record InstalledOfflineRoutingManifest(
    DateTime UpdatedAtUtc,
    IReadOnlyList<InstalledOfflineRoutingBundle> Bundles)
{
    public static InstalledOfflineRoutingManifest Empty { get; } = new(DateTime.UtcNow, []);
}
