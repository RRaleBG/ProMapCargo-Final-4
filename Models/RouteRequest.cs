using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models;

public sealed class RouteRequest
{
    [JsonPropertyName("start")]
    public GeoPoint Start { get; init; } =
        new(0, 0);

    [JsonPropertyName("destination")]
    public GeoPoint? Destination { get; init; }

  
    [JsonPropertyName("end")]
    public GeoPoint? End { get; init; }

    [JsonPropertyName("profile")]
    public string Profile { get; init; } =
        "truck";

    [JsonPropertyName("avoidRestricted")]
    public bool AvoidRestricted { get; init; } =
        true;

    [JsonPropertyName("truck")]
    public TruckProfile? Truck { get; init; }

    [JsonPropertyName("departureAt")]
    public DateTimeOffset? DepartureAt { get; init; }

    [JsonIgnore]
    public GeoPoint Target =>  Destination ?? End ?? new GeoPoint(0, 0);
}