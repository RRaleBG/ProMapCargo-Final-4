using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
namespace ProMapCargo.OsmImporter;
public sealed class ImportDbContext(DbContextOptions<ImportDbContext> options) : DbContext(options)
{
    public DbSet<RoadRestriction> RoadRestrictions => Set<RoadRestriction>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("postgis");
        b.Entity<RoadRestriction>(e =>
        {
            e.ToTable("road_restrictions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Geometry).HasColumnType("geometry(Geometry,4326)");
            e.HasIndex(x => x.Geometry).HasMethod("gist");
            e.HasIndex(x => x.OsmWayId);
        }
        );
    }
}
public sealed class RoadRestriction
{
    public long Id { get; set; }
    public long OsmWayId { get; set; }
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
    public string? ConditionalRaw { get; set; }
    public Geometry Geometry { get; set; } = default!;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
