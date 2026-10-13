using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>Data of /dispatch (Command Center): fleet on the map, active trips, events, fleet status, borders.</summary>
public sealed class DispatchQueries(ProMapCargoDbContext db, ICurrentUserContext current, UserPresenceTracker presence)
{
    /// <summary>Null when the request has no company context.</summary>
    public async Task<object?> OverviewAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;

        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == c && x.Type != VehicleType.Trailer).ToListAsync(ct);
        var drivers = await db.Drivers.AsNoTracking().Where(x => x.CompanyId == c).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var trips = await db.Trips.AsNoTracking().Where(x => x.CompanyId == c && x.Status != TripStatus.Cancelled).ToListAsync(ct);
        var running = trips.Where(x => x.Status is TripStatus.Active or TripStatus.Paused or TripStatus.Planned).ToList();
        var orderIds = running.Select(x => x.TransportOrderId).ToList();
        var stops = await db.TransportStops.AsNoTracking().Where(x => x.CompanyId == c && orderIds.Contains(x.TransportOrderId)).OrderBy(x => x.Sequence).ToListAsync(ct);
        var alerts = await db.OperationalAlerts.AsNoTracking().Where(x => x.CompanyId == c).OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
        var borders = await db.BorderCrossings.AsNoTracking()
            .Where(x => x.CompanyId == c && (x.Status == BorderCrossingStatus.Approaching || x.Status == BorderCrossingStatus.Queued || x.Status == BorderCrossingStatus.Inspection))
            .ToListAsync(ct);
        var dispatcherIds = await (from ur in db.UserRoles
                                   join r in db.Roles on ur.RoleId equals r.Id
                                   join u in db.Users on ur.UserId equals u.Id
                                   where u.CompanyId == c && (r.Name == "Dispatcher" || r.Name == "Administrator")
                                   select u.Id).Distinct().ToListAsync(ct);

        string Route(Trip t)
        {
            var own = stops.Where(s => s.TransportOrderId == t.TransportOrderId).ToList();
            return own.Count == 0 ? "" : $"{own[0].City ?? own[0].Name} → {own[^1].City ?? own[^1].Name}";
        }

        var openAlerts = alerts.Where(x => x.Status == "Open").ToList();
        var activeVehicleIds = running.Where(t => t.Status != TripStatus.Planned).Select(t => t.VehicleId).ToHashSet();
        var loadingVehicleIds = running.Where(t => t.ExecutionState is TripExecutionState.Loading or TripExecutionState.Unloading or TripExecutionState.ReadyForDeparture).Select(t => t.VehicleId).ToHashSet();

        return (new
        {
            Kpi = new
            {
                ActiveTrips = running.Count(t => t.Status == TripStatus.Active),
                Dispatchers = dispatcherIds.Count,
                DispatchersOnline = dispatcherIds.Count(presence.IsOnline),
                Vehicles = vehicles.Count,
                Delays = openAlerts.Count(x => x.Type == "Delay"),
                Borders = borders.Count,
                Incidents = openAlerts.Count(x => x.Type == "Incident"),
            },
            Fleet = vehicles.Select(v =>
            {
                var trip = running.FirstOrDefault(t => t.VehicleId == v.Id && t.Status != TripStatus.Planned);
                return new
                {
                    v.Id,
                    v.Registration,
                    Driver = v.CurrentDriverId is { } d && drivers.TryGetValue(d, out var name) ? name : null,
                    Status = v.Status == VehicleStatus.Maintenance ? "maintenance" : trip is null ? "available" : "active",
                    HasAlert = openAlerts.Any(a => a.VehicleId == v.Id),
                    Lat = trip?.CurrentLatitude,
                    Lon = trip?.CurrentLongitude,
                    SpeedKmh = trip?.CurrentSpeedKmh,
                    TripId = trip?.Id,
                    Route = trip is null ? null : Route(trip),
                };
            }),
            Trips = running.OrderByDescending(t => t.Status == TripStatus.Active).ThenBy(t => t.PlannedStartAt).Select(t => new
            {
                t.Id,
                t.TripNumber,
                Vehicle = vehicles.FirstOrDefault(v => v.Id == t.VehicleId)?.Registration,
                Driver = t.DriverId is { } d && drivers.TryGetValue(d, out var name) ? name : null,
                Route = Route(t),
                Status = t.Status.ToString(),
                State = t.ExecutionState.ToString(),
                t.RouteProgressPercent,
                Eta = t.PlannedArrivalAt,
                t.PlannedStartAt,
            }),
            Events = alerts.Take(20).Select(a => new
            {
                a.Id,
                a.Type,
                a.Severity,
                a.Status,
                a.Message,
                a.CreatedAt,
                Vehicle = vehicles.FirstOrDefault(v => v.Id == a.VehicleId)?.Registration,
            }),
            FleetStatus = new
            {
                InTransit = activeVehicleIds.Count - loadingVehicleIds.Count(activeVehicleIds.Contains),
                Loading = loadingVehicleIds.Count,
                Available = vehicles.Count(v => v.Status == VehicleStatus.Active && !activeVehicleIds.Contains(v.Id)),
                Maintenance = vehicles.Count(v => v.Status is VehicleStatus.Maintenance or VehicleStatus.OutOfService),
            },
            Borders = borders.GroupBy(x => new { x.BorderPoint, x.CountryFrom, x.CountryTo }).Select(g => new
            {
                g.Key.BorderPoint,
                g.Key.CountryFrom,
                g.Key.CountryTo,
                Vehicles = g.Count(),
                WaitingMinutes = g.Max(x => x.WaitingMinutes),
            }),
        });
    }
}
