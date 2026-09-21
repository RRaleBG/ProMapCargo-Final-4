using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models;

public sealed record GeoPoint(
    [property: JsonPropertyName("latitude")]
    double Lat,

    [property: JsonPropertyName("longitude")]
    double Lon
);