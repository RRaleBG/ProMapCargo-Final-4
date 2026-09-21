namespace ProMapCargo.Api.Models;

public sealed record GeocodingResult(
    double Lat,
    double Lon,
    string DisplayName,
    string? Type,
    string? Category,
    string? OsmType,
    long? OsmId,
    double? Importance);