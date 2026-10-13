using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>Data of the alert center (/alerts): alerts with the vehicle, driver and trip they concern.</summary>
public sealed class AlertQueries(ProMapCargoDbContext db, ICurrentUserContext current)
{
    /// <summary>Null when the request has no company context.</summary>
    public async Task<object?> FeedAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid companyId)
            return null;

        var alerts = await db.OperationalAlerts.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.Registration, ct);
        var drivers = await db.Drivers.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var trips = await db.Trips.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => new { x.TripNumber, x.CurrentLatitude, x.CurrentLongitude }, ct);

        return (alerts.Select(x => new
        {
            x.Id,
            x.Type,
            x.Severity,
            x.Status,
            x.Message,
            x.Source,
            x.CreatedAt,
            x.ResolvedAt,
            x.MetadataJson,
            Vehicle = x.VehicleId is { } v && vehicles.TryGetValue(v, out var registration) ? registration : null,
            Driver = x.DriverId is { } d && drivers.TryGetValue(d, out var name) ? name : null,
            Trip = x.TripId is { } t && trips.TryGetValue(t, out var trip) ? trip.TripNumber : null,
            Latitude = x.TripId is { } t2 && trips.TryGetValue(t2, out var position) ? position.CurrentLatitude : null,
            Longitude = x.TripId is { } t3 && trips.TryGetValue(t3, out var position2) ? position2.CurrentLongitude : null,
        }));
    }
}
