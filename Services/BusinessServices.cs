using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class BusinessService(ProMapCargoDbContext db, ICurrentUserContext current, IHubContext<NavigationHub> hub)
{
    private Guid GetRequiredCompanyId() =>
        current.CompanyId ?? throw new UnauthorizedAccessException("Company context is required.");

    public Task<List<Vehicle>> VehiclesAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.Vehicles
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Registration)
            .ToListAsync(ct);
    }

    public Task<List<Driver>> DriversAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.Drivers
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.FullName)
            .ToListAsync(ct);
    }

    public Task<List<TransportOrder>> OrdersAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.TransportOrders
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
    }

    public Task<List<Trip>> TripsAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.Trips
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartedAt)
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<object> DashboardAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        var trips = await db.Trips
            .Where(x => x.CompanyId == companyId
                        && x.Status != TripStatus.Completed
                        && x.Status != TripStatus.Cancelled)
            .ToListAsync(ct);

        var vehicles = await db.Vehicles
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(ct);

        var alerts = await db.OperationalAlerts
            .Where(x => x.CompanyId == companyId && x.Status != "Resolved")
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            kpi = new
            {
                activeTrips = trips.Count,
                vehiclesOnline = vehicles.Count(x => x.Status == VehicleStatus.Active),
                gpsLost = trips.Count(x =>
                    x.LastGpsAt is null || x.LastGpsAt < DateTimeOffset.UtcNow.AddMinutes(-2)),
                offRoute = trips.Count(x => x.IsOffRoute),
                alerts = alerts.Count
            },
            trips,
            alerts,
            vehicles
        };
    }

    public Task<TripRoute?> ActiveRouteAsync(Guid tripId, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.TripRoutes
            .Where(x => x.CompanyId == companyId
                        && x.TripId == tripId
                        && x.Status == TripRouteStatus.Active)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RouteDispatch> DispatchAsync(
        Guid tripId,
        Guid routeId,
        CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        var trip = await db.Trips.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == tripId,
            ct) ?? throw new KeyNotFoundException("Trip not found");

        var route = await db.TripRoutes.SingleOrDefaultAsync(
            x => x.CompanyId == companyId
                 && x.Id == routeId
                 && x.TripId == tripId
                 && x.Status == TripRouteStatus.Active,
            ct) ?? throw new KeyNotFoundException("Active route not found");

        if (!trip.DriverId.HasValue || !trip.VehicleId.HasValue)
        {
            throw new InvalidOperationException("Trip must have driver and vehicle.");
        }

        await db.RouteDispatches
            .Where(x => x.CompanyId == companyId
                        && x.TripId == tripId
                        && x.Status != RouteDispatchStatus.Rejected
                        && x.Status != RouteDispatchStatus.Cancelled)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    x => x.Status,
                    RouteDispatchStatus.Superseded),
                ct);

        var dispatch = new RouteDispatch
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            TripId = tripId,
            TripRouteId = route.Id,
            DriverId = trip.DriverId.Value,
            VehicleId = trip.VehicleId.Value,
            Status = RouteDispatchStatus.Sent,
            SentAt = DateTimeOffset.UtcNow,
            SentByUserId = current.UserId
        };

        db.RouteDispatches.Add(dispatch);
        await db.SaveChangesAsync(ct);

        await hub.Clients
            .Group($"company:{companyId}")
            .SendAsync(
                "routeDispatched",
                new
                {
                    dispatch.Id,
                    dispatch.TripId,
                    dispatch.TripRouteId,
                    dispatch.DriverId,
                    dispatch.VehicleId
                },
                ct);

        await hub.Clients
            .Group($"driver:{dispatch.DriverId}")
            .SendAsync(
                "routeDispatched",
                new { dispatch.Id, dispatch.TripId, dispatch.TripRouteId },
                ct);

        return dispatch;
    }
}
