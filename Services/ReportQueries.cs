using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>Key figures of /reports.</summary>
public sealed class ReportQueries(ProMapCargoDbContext db, ICurrentUserContext current)
{
    /// <summary>Null when the request has no company context.</summary>
    public async Task<object?> KpiAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;

        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == c && x.Type != VehicleType.Trailer && x.Status == VehicleStatus.Active).CountAsync(ct);
        var busy = await db.Trips.AsNoTracking().Where(x => x.CompanyId == c && (x.Status == TripStatus.Active || x.Status == TripStatus.Paused)).Select(x => x.VehicleId).Distinct().CountAsync(ct);
        var unloads = await db.TransportStops.AsNoTracking()
            .Where(x => x.CompanyId == c && x.Type == TransportStopType.Unloading && x.Status == TransportStopStatus.Completed && x.PlannedAt != null && x.ArrivedAt != null)
            .Select(x => new { x.PlannedAt, x.ArrivedAt })
            .ToListAsync(ct);
        var completed = await db.Trips.AsNoTracking()
            .Where(x => x.CompanyId == c && x.Status == TripStatus.Completed && x.PlannedDistanceMeters > 0)
            .Select(x => new { x.PlannedDistanceMeters, x.RouteCoveredMeters })
            .ToListAsync(ct);

        return (new
        {
            FleetUtilization = vehicles == 0 ? 0 : Math.Round(100.0 * busy / vehicles, 1),
            OnTimeDeliveries = unloads.Count == 0 ? (double?)null : Math.Round(100.0 * unloads.Count(x => x.ArrivedAt <= x.PlannedAt!.Value.AddMinutes(30)) / unloads.Count, 1),
            RouteDeviation = completed.Count == 0 ? (double?)null : Math.Round(completed.Average(x => Math.Abs(x.RouteCoveredMeters - x.PlannedDistanceMeters) / x.PlannedDistanceMeters) * 100, 1),
        });
    }
}
