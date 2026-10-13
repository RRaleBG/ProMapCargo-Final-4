using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>/reports: key figures and the four report types, as JSON or CSV.</summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/reports")]
public sealed class ReportsController(ProMapCargoDbContext db, ICurrentUserContext current) : ControllerBase
{
    [HttpGet("kpi")]
    public async Task<ActionResult<object>> Kpi(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();

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

        return Ok(new
        {
            FleetUtilization = vehicles == 0 ? 0 : Math.Round(100.0 * busy / vehicles, 1),
            OnTimeDeliveries = unloads.Count == 0 ? (double?)null : Math.Round(100.0 * unloads.Count(x => x.ArrivedAt <= x.PlannedAt!.Value.AddMinutes(30)) / unloads.Count, 1),
            RouteDeviation = completed.Count == 0 ? (double?)null : Math.Round(completed.Average(x => Math.Abs(x.RouteCoveredMeters - x.PlannedDistanceMeters) / x.PlannedDistanceMeters) * 100, 1),
        });
    }

    [HttpGet("{type}")]
    public async Task<IActionResult> Generate(string type, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();

        var start = new DateTimeOffset((from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30))).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset((to ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        if (end < start) return BadRequest(new { code = "ValidationError", message = "Datum \"do\" ne može biti pre datuma \"od\"." });

        var (title, columns, rows) = type switch
        {
            "fleet" => await FleetAsync(c, start, end, ct),
            "trips" => await TripsAsync(c, start, end, ct),
            "drivers" => await DriversAsync(c, ct),
            "routing" => await RoutingAsync(c, start, end, ct),
            _ => (null, Array.Empty<string>(), new List<string[]>()),
        };
        if (title is null) return NotFound(new { code = "NotFound", message = "Nepoznat tip izveštaja." });

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = new StringBuilder();
            foreach (var line in rows.Prepend(columns))
            {
                csv.AppendLine(string.Join(';', line.Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"")));
            }

            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", $"izvestaj-{type}-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        return Ok(new { Title = title, From = start, To = end, Columns = columns, Rows = rows });
    }

    private async Task<(string?, string[], List<string[]>)> FleetAsync(Guid c, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == c).OrderBy(x => x.Registration).ToListAsync(ct);
        var trips = await db.Trips.AsNoTracking().Where(x => x.CompanyId == c && x.CreatedAt <= end && (x.CompletedAt == null || x.CompletedAt >= start)).ToListAsync(ct);
        var costs = await db.TransportCosts.AsNoTracking().Where(x => x.CompanyId == c && x.Status != TransportCostStatus.Rejected && (x.IncurredAt ?? x.CreatedAt) >= start && (x.IncurredAt ?? x.CreatedAt) <= end).ToListAsync(ct);

        return ("Performanse flote", ["Registracija", "Vozilo", "Tip", "Status", "Ture", "Planirano km", "Pređeno km", "Troškovi EUR", "Kilometraža"],
            vehicles.Select(v =>
            {
                var own = trips.Where(t => t.VehicleId == v.Id).ToList();
                return new[]
                {
                    v.Registration, $"{v.Make} {v.Model}".Trim(), v.Type.ToString(), v.Status.ToString(), own.Count.ToString(),
                    (own.Sum(t => t.PlannedDistanceMeters) / 1000).ToString("0"), (own.Sum(t => t.RouteCoveredMeters) / 1000).ToString("0"),
                    costs.Where(x => x.VehicleId == v.Id).Sum(x => x.Amount).ToString("0.00"), v.OdometerKm?.ToString() ?? "",
                };
            }).ToList());
    }

    private async Task<(string?, string[], List<string[]>)> TripsAsync(Guid c, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var trips = await db.Trips.AsNoTracking().Where(x => x.CompanyId == c && x.CreatedAt <= end && (x.CompletedAt == null || x.CompletedAt >= start)).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var orders = await db.TransportOrders.AsNoTracking().Where(x => x.CompanyId == c).ToDictionaryAsync(x => x.Id, ct);
        var costs = await db.TransportCosts.AsNoTracking().Where(x => x.CompanyId == c && x.TripId != null && x.Status != TransportCostStatus.Rejected
                                                                     && x.Category != TransportCostCategory.Payment && x.Category != TransportCostCategory.Allowance).ToListAsync(ct);

        return ("Profitabilnost ture (budžet i troškovi)", ["Tura", "Nalog", "Kupac", "Status", "Budžet EUR", "Troškovi EUR", "Razlika EUR", "Km planirano"],
            trips.Select(t =>
            {
                var spent = costs.Where(x => x.TripId == t.Id).Sum(x => x.Amount);
                orders.TryGetValue(t.TransportOrderId, out var order);
                return new[]
                {
                    t.TripNumber ?? t.Id.ToString()[..8], order?.OrderNumber ?? "", order?.CustomerName ?? "", t.Status.ToString(),
                    (t.BudgetAmount ?? 0).ToString("0.00"), spent.ToString("0.00"), ((t.BudgetAmount ?? 0) - spent).ToString("0.00"),
                    (t.PlannedDistanceMeters / 1000).ToString("0"),
                };
            }).ToList());
    }

    private async Task<(string?, string[], List<string[]>)> DriversAsync(Guid c, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string State(DateOnly? d) => d is null ? "nema" : d < today ? $"ISTEKLO {d:dd.MM.yyyy}" : d <= today.AddDays(30) ? $"ističe {d:dd.MM.yyyy}" : $"{d:dd.MM.yyyy}";
        var drivers = await db.Drivers.AsNoTracking().Where(x => x.CompanyId == c).OrderBy(x => x.FullName).ToListAsync(ct);

        return ("Usklađenost vozača", ["Vozač", "Broj", "Status", "Vozačka dozvola", "Kategorije", "Kod 95", "ADR", "Tahograf kartica", "Lekarski"],
            drivers.Select(d => new[]
            {
                d.FullName, d.EmployeeNumber ?? "", d.Status.ToString(), State(d.LicenseExpiry), d.LicenseCategories ?? "",
                State(d.Code95Expiry), State(d.AdrExpiry), State(d.TachographCardExpiry), State(d.MedicalExpiry),
            }).ToList());
    }

    private async Task<(string?, string[], List<string[]>)> RoutingAsync(Guid c, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var requests = await db.ModerationRequests.AsNoTracking()
            .Where(x => x.CompanyId == c && x.CreatedAt >= start && x.CreatedAt <= end && (x.Domain == "routing" || x.Domain == "eta"))
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var alerts = await db.OperationalAlerts.AsNoTracking()
            .Where(x => x.CompanyId == c && x.CreatedAt >= start && x.CreatedAt <= end && (x.Type == "OffRoute" || x.Type == "Delay" || x.Type == "Border"))
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

        return ("Routing izuzeci", ["Vreme", "Izvor", "Vrsta", "Opis", "Status"],
            requests.Select(x => new[] { x.CreatedAt.ToString("yyyy-MM-dd HH:mm"), "Moderacija " + x.Number, x.Title, x.Description ?? "", x.Status })
                .Concat(alerts.Select(x => new[] { x.CreatedAt.ToString("yyyy-MM-dd HH:mm"), "Upozorenje", x.Type, x.Message, x.Status }))
                .OrderByDescending(x => x[0])
                .ToList());
    }
}
