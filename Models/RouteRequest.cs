using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models;

public sealed class RouteRequest
{
    private static readonly GeoPoint EmptyPoint = new(0, 0);

    [JsonPropertyName("start")]
    public GeoPoint? Start { get; init; }

    [JsonPropertyName("destination")]
    public GeoPoint? Destination { get; init; }

    [JsonPropertyName("end")]
    public GeoPoint? End { get; init; }

    [JsonPropertyName("startLat")]
    public double? StartLat { get; init; }

    [JsonPropertyName("startLon")]
    public double? StartLon { get; init; }

    [JsonPropertyName("endLat")]
    public double? EndLat { get; init; }

    [JsonPropertyName("endLon")]
    public double? EndLon { get; init; }

    [JsonPropertyName("profile")]
    public string Profile { get; init; } = "truck";

    [JsonPropertyName("avoidRestricted")]
    public bool AvoidRestricted { get; init; } = true;

    [JsonPropertyName("truck")]
    public TruckProfile? Truck { get; init; }

    [JsonPropertyName("truckProfile")]
    public TruckProfile? TruckProfile { get; init; }

    [JsonPropertyName("departureAt")]
    public DateTimeOffset? DepartureAt { get; init; }

    [JsonIgnore]
    public GeoPoint ResolvedStart =>
        Start
        ?? (StartLat.HasValue && StartLon.HasValue
            ? new GeoPoint(StartLat.Value, StartLon.Value)
            : EmptyPoint);

    [JsonIgnore]
    public GeoPoint Target =>
        Destination
        ?? End
        ?? (EndLat.HasValue && EndLon.HasValue
            ? new GeoPoint(EndLat.Value, EndLon.Value)
            : EmptyPoint);

    [JsonIgnore]
    public TruckProfile? ResolvedTruck => Truck ?? TruckProfile;
}
