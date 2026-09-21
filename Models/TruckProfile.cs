using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models;

public sealed class TruckProfile
{
    [JsonPropertyName("grossWeightT")]
    public decimal GrossWeightTons { get; init; }

    [JsonPropertyName("heightM")]
    public decimal HeightMeters { get; init; }

    [JsonPropertyName("widthM")]
    public decimal WidthMeters { get; init; }

    [JsonPropertyName("lengthM")]
    public decimal LengthMeters { get; init; }

    [JsonPropertyName("axleLoadT")]
    public decimal? AxleLoadTons { get; init; }

    [JsonPropertyName("axles")]
    public int Axles { get; init; } = 5;

    [JsonPropertyName("isHgv")]
    public bool IsHgv { get; init; } = true;

    [JsonPropertyName("commercial")]
    public bool Commercial { get; init; } = true;

    [JsonPropertyName("hazmat")]
    public bool Hazmat { get; init; }

    [JsonPropertyName("goods")]
    public string? Goods { get; init; }

    [JsonPropertyName("adrClass")]
    public string? AdrClass { get; init; }

    [JsonPropertyName("vehicleClass")]
    public string VehicleClass { get; init; } =
        "HeavyGoods";

    [JsonPropertyName("maxSpeedKmh")]
    public decimal MaxSpeedKmh { get; init; } =
        90;
}