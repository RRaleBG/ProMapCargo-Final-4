using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models;

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
    [property: JsonPropertyName("generatedAtUtc")] DateTime GeneratedAtUtc,
    // SHA-256 of the places file (checksumSha256 is the graph file's); null when unknown.
    [property: JsonPropertyName("placesChecksumSha256")] string? PlacesChecksumSha256 = null);
