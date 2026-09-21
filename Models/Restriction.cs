namespace ProMapCargo.Api.Models;
public sealed class Restriction
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public decimal? MaxWeightTons { get; init; }
    public decimal? MaxAxleLoadTons { get; init; }
    public decimal? MaxHeightMeters { get; init; }
    public decimal? MaxWidthMeters { get; init; }
    public decimal? MaxLengthMeters { get; init; }
    public bool HgvBan { get; init; }
    public bool HazmatBan { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public double[] Center { get; init; } = [0, 0];
    // lat, lon
    public double RadiusMeters { get; init; } = 120;
    public List<double[]> Polygon { get; init; } = [];
}
