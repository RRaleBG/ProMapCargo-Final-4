using NetTopologySuite.Geometries;
namespace ProMapCargo.Api.Models;
public sealed class RoadRestriction
{
    public long Id { get; set; }
    public long? OsmWayId { get; set; }
    public string Name { get; set; } = "";
    public string RestrictionType { get; set; } = "";
    public decimal? MaxWeightTons { get; set; }
    public decimal? MaxHeightMeters { get; set; }
    public decimal? MaxWidthMeters { get; set; }
    public decimal? MaxLengthMeters { get; set; }
    public decimal? MaxAxleLoadTons { get; set; }
    public bool HgvBan { get; set; }
    public bool HazmatBan { get; set; }
    public string? AdrClass { get; set; }
    public TimeSpan? TimeFrom { get; set; }
    public TimeSpan? TimeTo { get; set; }
    public Geometry Geometry { get; set; } = default!;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
