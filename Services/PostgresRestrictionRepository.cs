using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Services;
public interface IPostgresRestrictionRepository
{
    Task<List<RoadRestriction>> FindNearRouteAsync(
    IReadOnlyCollection<GeoPoint> routePoints,
    double bufferMeters,
    CancellationToken ct);
}
public sealed class PostgresRestrictionRepository(ProMapCargoDbContext db)
: IPostgresRestrictionRepository
{
    public async Task<List<RoadRestriction>> FindNearRouteAsync(
    IReadOnlyCollection<GeoPoint> routePoints,
    double bufferMeters,
    CancellationToken ct)
    {
        if (routePoints.Count == 0) return [];
        var coordinates = routePoints
        .Select(p => new Coordinate(p.Lon, p.Lat))
        .ToArray();
        var line = new LineString(coordinates)
        {
            SRID = 4326
        }
        ;
        // Transform to Web Mercator only for the metric buffer.
        // The stored geometry remains EPSG:4326.
        var query = db.RoadRestrictions
        .Where(r => r.Geometry != null &&
        r.Geometry.IsWithinDistance(line, bufferMeters / 111_320.0));
        return await query.AsNoTracking().ToListAsync(ct);
    }
}
