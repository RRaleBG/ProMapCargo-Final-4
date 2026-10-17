using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>Portal data, or null when the account is not linked to a driver.</summary>
public sealed record DriverPortal(object? Data);

/// <summary>Data of the /driver portal: active trip, stops and the dispatch phone.</summary>
public sealed class DriverPortalQueries(ProMapCargoDbContext db, ICurrentUserContext current)
{
    /// <summary>Null when the request has no company context.</summary>
    public async Task<DriverPortal?> PortalAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null) return new DriverPortal(null);

        var trip = await ActiveTripAsync(c, driver.Id, ct);
        var stops = trip is null ? [] : await db.TransportStops.AsNoTracking()
            .Where(x => x.CompanyId == c && x.TransportOrderId == trip.TransportOrderId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(ct);
        var next = stops.FirstOrDefault(x => x.Status is TransportStopStatus.Planned or TransportStopStatus.Arrived or TransportStopStatus.InService);
        var vehicle = trip?.VehicleId is { } vehicleId ? await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == vehicleId, ct) : null;
        var phone = await db.Companies.AsNoTracking().Where(x => x.Id == c).Select(x => x.DispatchPhone).FirstOrDefaultAsync(ct);
        // A route sent by dispatch that the driver has not answered yet (accept / reject in the app).
        var pendingRoute = await db.RouteDispatches.AsNoTracking()
            .Where(x => x.CompanyId == c && x.DriverId == driver.Id && (x.Status == RouteDispatchStatus.Pending || x.Status == RouteDispatchStatus.Sent))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.TripId, Status = x.Status.ToString(), x.CreatedAt, x.SentAt })
            .FirstOrDefaultAsync(ct);

        return new DriverPortal(new
        {
            Driver = new { driver.Id, driver.FullName, driver.EmployeeNumber, Status = driver.Status.ToString(), driver.CurrentVehicleId },
            Trip = trip is null ? null : new
            {
                trip.Id,
                trip.TripNumber,
                Status = trip.Status.ToString(),
                State = trip.ExecutionState.ToString(),
                trip.RouteProgressPercent,
                trip.PlannedArrivalAt,
                Route = stops.Count == 0 ? "" : (stops[0].City ?? stops[0].Name) + " → " + (stops[^1].City ?? stops[^1].Name),
                Vehicle = vehicle?.Registration,
                // The phone sends its GPS position for this vehicle and trip.
                trip.VehicleId,
            },
            Stops = stops.Select(x => new
            {
                x.Id, x.Sequence, Type = x.Type.ToString(), Status = x.Status.ToString(), x.Name, x.City, x.CountryCode,
                x.PlannedAt, x.ArrivedAt, x.CompletedAt, x.Notes,
                // Navigation to the stop from the app.
                x.Address, x.Latitude, x.Longitude,
            }),
            NextStopId = next?.Id,
            DispatchPhone = phone,
            PendingRoute = pendingRoute,
        });
    }

    /// <summary>The driver's running trip, else the next planned one.</summary>
    public Task<Trip?> ActiveTripAsync(Guid c, Guid driverId, CancellationToken ct, bool tracked = false)
    {
        var trips = tracked ? db.Trips : db.Trips.AsNoTracking();
        return trips.Where(x => x.CompanyId == c && x.DriverId == driverId && (x.Status == TripStatus.Active || x.Status == TripStatus.Paused || x.Status == TripStatus.Planned))
            .OrderByDescending(x => x.Status == TripStatus.Active || x.Status == TripStatus.Paused)
            .ThenBy(x => x.PlannedStartAt)
            .FirstOrDefaultAsync(ct);
    }
}
