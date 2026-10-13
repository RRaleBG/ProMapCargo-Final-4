using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>Data of /moderation: change requests with their recorded decisions.</summary>
public sealed class ModerationQueries(ProMapCargoDbContext db, ICurrentUserContext current)
{
    /// <summary>Null when the request has no company context.</summary>
    public async Task<object?> ListAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;

        var requests = await db.ModerationRequests.AsNoTracking().Where(x => x.CompanyId == c).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == c).ToDictionaryAsync(x => x.Id, x => x.Registration, ct);
        var trips = await db.Trips.AsNoTracking().Where(x => x.CompanyId == c).ToDictionaryAsync(x => x.Id, x => x.TripNumber, ct);

        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var decided = requests.Where(x => x.DecidedAt != null).ToList();

        return (new
        {
            Kpi = new
            {
                Pending = requests.Count(x => x.Status == "pending"),
                HighRisk = requests.Count(x => x.Status == "pending" && x.Risk == "high"),
                ApprovedMonth = decided.Count(x => x.Status == "approved" && x.DecidedAt >= monthStart),
                RejectedMonth = decided.Count(x => x.Status == "rejected" && x.DecidedAt >= monthStart),
                Today = requests.Count(x => x.CreatedAt.UtcDateTime.Date == DateTime.UtcNow.Date),
                AverageMinutes = decided.Count == 0 ? 0 : (int)Math.Round(decided.Average(x => (x.DecidedAt!.Value - x.CreatedAt).TotalMinutes)),
            },
            Requests = requests.Select(x => new
            {
                x.Id,
                x.Number,
                x.Title,
                x.Description,
                x.Domain,
                x.Risk,
                x.Status,
                x.RequestedByName,
                x.ImpactSummary,
                x.CreatedAt,
                x.DecidedAt,
                x.DecidedByName,
                x.DecisionNote,
                Vehicle = x.VehicleId is { } v && vehicles.TryGetValue(v, out var registration) ? registration : null,
                Trip = x.TripId is { } t && trips.TryGetValue(t, out var number) ? number : null,
            }),
        });
    }
}
