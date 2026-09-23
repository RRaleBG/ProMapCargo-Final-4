using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models;

public sealed class GeoPoint
{
    [JsonPropertyName("latitude")]
    [JsonInclude]
    public double Lat { get; init; }

    [JsonPropertyName("longitude")]
    [JsonInclude]
    public double Lon { get; init; }

    [JsonPropertyName("lat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double LatAlias
    {
        init => Lat = value;
    }

    [JsonPropertyName("lon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double LonAlias
    {
        init => Lon = value;
    }

    public GeoPoint()
    {
    }

    public GeoPoint(double lat, double lon)
    {
        Lat = lat;
        Lon = lon;
    }
}
