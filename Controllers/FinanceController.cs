using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>
/// /finance: a driver's balance, the budget of the active trip, fuel card and the cost
/// ledger; "Novi trošak" saves a cost with its receipt; dispatch approves or rejects it.
/// </summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/finance")]
public sealed class FinanceController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit, FileStorageService files) : ControllerBase
{
    // Paid by the driver: the company owes it back (with allowances, minus payouts).
    private static readonly string[] DriverPaid = ["cash", "driver-card"];

    [HttpGet("overview")]
    public async Task<ActionResult<object>> Overview([FromQuery] Guid? driverId, [FromQuery] string mode = "driver", CancellationToken ct = default)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();

        var drivers = await db.Drivers.AsNoTracking().Where(x => x.CompanyId == c).OrderBy(x => x.FullName).ToListAsync(ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == c).ToListAsync(ct);
        var trips = await db.Trips.AsNoTracking().Where(x => x.CompanyId == c).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var costs = await db.TransportCosts.AsNoTracking().Where(x => x.CompanyId == c).OrderByDescending(x => x.IncurredAt ?? x.CreatedAt).Take(1000).ToListAsync(ct);
        var orderIds = trips.Select(x => x.TransportOrderId).Distinct().ToList();
        var stops = await db.TransportStops.AsNoTracking().Where(x => x.CompanyId == c && orderIds.Contains(x.TransportOrderId)).OrderBy(x => x.Sequence).ToListAsync(ct);

        // Own view of a driver account; otherwise the chosen driver or the one on an active trip.
        var me = drivers.FirstOrDefault(x => x.UserId == current.UserId);
        var driver = me
                     ?? drivers.FirstOrDefault(x => x.Id == driverId)
                     ?? drivers.FirstOrDefault(x => trips.Any(t => t.DriverId == x.Id && t.Status == TripStatus.Active))
                     ?? drivers.FirstOrDefault();
        var dispatchMode = me is null && string.Equals(mode, "dispatch", StringComparison.OrdinalIgnoreCase);

        string Route(Trip trip) => string.Join(" → ", stops.Where(s => s.TransportOrderId == trip.TransportOrderId).Select(s => s.City ?? s.Name));
        string? Registration(Guid? id) => vehicles.FirstOrDefault(v => v.Id == id)?.Registration;
        string? TripNumber(Guid? id) => trips.FirstOrDefault(t => t.Id == id)?.TripNumber;

        var driverTrips = trips.Where(t => driver is not null && t.DriverId == driver.Id).ToList();
        var activeTrip = driverTrips.FirstOrDefault(t => t.Status is TripStatus.Active or TripStatus.Paused)
                         ?? driverTrips.FirstOrDefault(t => t.Status == TripStatus.Planned);
        var previousTrip = driverTrips.Where(t => t.Status == TripStatus.Completed).OrderByDescending(t => t.CompletedAt).FirstOrDefault();

        var scoped = dispatchMode ? costs : costs.Where(x => driver is not null && x.DriverId == driver.Id).ToList();
        var counted = scoped.Where(x => x.Status != TransportCostStatus.Rejected).ToList();

        decimal Spent(Trip? trip) => trip is null ? 0 : costs
            .Where(x => x.TripId == trip.Id && x.Status != TransportCostStatus.Rejected && x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment))
            .Sum(x => x.Amount);

        decimal Balance(IEnumerable<TransportCost> items) => items.Sum(x =>
            x.Category == TransportCostCategory.Payment ? -x.Amount
            : x.Category == TransportCostCategory.Allowance || DriverPaid.Contains(x.PaymentMethod) ? x.Amount
            : 0);

        var settled = counted.Where(x => x.Status is TransportCostStatus.Approved or TransportCostStatus.Paid).ToList();
        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var activeCosts = activeTrip is null ? [] : counted.Where(x => x.TripId == activeTrip.Id).ToList();

        object? TripCard(Trip? trip) => trip is null ? null : new
        {
            trip.Id,
            Number = trip.TripNumber,
            Route = Route(trip),
            Status = trip.Status.ToString(),
            Vehicle = Registration(trip.VehicleId),
            Driver = drivers.FirstOrDefault(d => d.Id == trip.DriverId)?.FullName,
            StartedAt = trip.StartedAt ?? trip.PlannedStartAt,
            trip.PlannedArrivalAt,
            trip.CompletedAt,
            Budget = trip.BudgetAmount,
            trip.Currency,
            Spent = Spent(trip),
            Remaining = (trip.BudgetAmount ?? 0) - Spent(trip),
            ByCategory = costs
                .Where(x => x.TripId == trip.Id && x.Status != TransportCostStatus.Rejected && x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment))
                .GroupBy(x => x.Category switch
                {
                    TransportCostCategory.Fuel or TransportCostCategory.AdBlue => "fuel",
                    TransportCostCategory.Toll => "toll",
                    TransportCostCategory.Parking => "parking",
                    _ => "other",
                })
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)),
        };

        var vehicle = vehicles.FirstOrDefault(v => v.Id == (activeTrip?.VehicleId ?? driver?.CurrentVehicleId));
        var fuel = activeCosts.Where(x => x.Category == TransportCostCategory.Fuel).ToList();

        return Ok(new
        {
            Mode = dispatchMode ? "dispatch" : "driver",
            IsDriverAccount = me is not null,
            CanReview = User.IsInRole("Administrator") || User.IsInRole("Dispatcher"),
            Driver = driver is null ? null : new { driver.Id, driver.FullName, driver.EmployeeNumber },
            Drivers = drivers.Select(d => new { d.Id, d.FullName }),
            Kpi = new
            {
                Balance = Balance(counted.Where(x => x.Status is TransportCostStatus.Approved or TransportCostStatus.Paid)),
                MonthDelta = Balance(counted.Where(x => x.Status is TransportCostStatus.Approved or TransportCostStatus.Paid && (x.IncurredAt ?? x.CreatedAt) >= monthStart)),
                ActiveRemaining = activeTrip is null ? (decimal?)null : (activeTrip.BudgetAmount ?? 0) - Spent(activeTrip),
                ActiveTripNumber = activeTrip?.TripNumber,
                PreviousNet = previousTrip is null ? (decimal?)null : (previousTrip.BudgetAmount ?? 0) - Spent(previousTrip),
                PreviousTripNumber = previousTrip?.TripNumber,
                TripCosts = Spent(activeTrip),
                TripCostCount = activeCosts.Count(x => x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment)),
                PendingAmount = scoped.Where(x => x.Status == TransportCostStatus.Pending).Sum(x => x.Amount),
                PendingCount = scoped.Count(x => x.Status == TransportCostStatus.Pending),
            },
            // Receivables: costs the driver paid and allowances; payouts reduce the balance.
            Balance = new
            {
                Receivables = settled.Where(x => x.Category == TransportCostCategory.Allowance || DriverPaid.Contains(x.PaymentMethod)).Sum(x => x.Amount),
                CashAdvances = 0m,
                Payments = settled.Where(x => x.Category == TransportCostCategory.Payment).Sum(x => x.Amount),
            },
            ActiveTrip = TripCard(activeTrip),
            PreviousTrip = TripCard(previousTrip),
            FuelCard = vehicle is null ? null : new
            {
                Label = vehicle.FuelCard,
                Vehicle = vehicle.Registration,
                Driver = driver?.FullName,
                Active = vehicle.Status != VehicleStatus.OutOfService,
                Liters = fuel.Sum(x => x.Quantity),
                Amount = fuel.Sum(x => x.Amount),
                AveragePrice = fuel.Sum(x => x.Quantity) > 0 ? fuel.Sum(x => x.Amount) / fuel.Sum(x => x.Quantity) : 0,
                Transactions = fuel.Select(x => new { x.Id, x.Location, x.CountryCode, At = x.IncurredAt ?? x.CreatedAt, x.CardLabel, x.Quantity, x.UnitPrice, x.Amount, Status = x.Status.ToString() }),
            },
            Transactions = scoped.Select(x => new
            {
                x.Id,
                Category = x.Category.ToString(),
                Status = x.Status.ToString(),
                x.Amount,
                x.Currency,
                At = x.IncurredAt ?? x.CreatedAt,
                x.Location,
                x.CountryCode,
                x.PaymentMethod,
                x.CardLabel,
                x.Quantity,
                x.Unit,
                x.UnitPrice,
                x.OdometerKm,
                x.Notes,
                x.TripId,
                TripNumber = TripNumber(x.TripId),
                Vehicle = Registration(x.VehicleId),
                Driver = drivers.FirstOrDefault(d => d.Id == x.DriverId)?.FullName,
                x.ReceiptFileId,
            }),
            Options = new
            {
                Trips = trips.Where(t => dispatchMode || driver is null || t.DriverId == driver.Id)
                    .Select(t => new { t.Id, t.TripNumber, t.VehicleId, Status = t.Status.ToString() }),
                Vehicles = vehicles.Where(v => v.Type != VehicleType.Trailer).OrderBy(v => v.Registration).Select(v => new { v.Id, v.Registration, v.OdometerKm }),
                Cards = vehicles.Where(v => !string.IsNullOrWhiteSpace(v.FuelCard)).OrderBy(v => v.FuelCard)
                    .Select(v => new { Label = v.FuelCard, Vehicle = v.Registration, Active = v.Status != VehicleStatus.OutOfService }),
            },
        });
    }

    [HttpPost("costs")]
    [RequestSizeLimit(FileStorageService.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<object>> AddCost([FromForm] CostForm form, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();

        if (!Enum.TryParse<TransportCostCategory>(form.Category, true, out var category))
            return BadRequest(new { code = "ValidationError", message = "Kategorija troška nije ispravna." });
        if (form.Amount is not > 0)
            return BadRequest(new { code = "ValidationError", message = "Iznos mora biti veći od nule." });

        var trip = form.TripId is { } tripId ? await db.Trips.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == tripId, ct) : null;
        if (form.TripId is not null && trip is null)
            return BadRequest(new { code = "ValidationError", message = "Izabrana tura ne postoji." });
        if (form.VehicleId is { } vehicleId && !await db.Vehicles.AnyAsync(x => x.CompanyId == c && x.Id == vehicleId, ct))
            return BadRequest(new { code = "ValidationError", message = "Izabrano vozilo ne postoji." });

        // A driver account records its own costs; dispatch records them for the trip's driver.
        var driverId = await db.Drivers.Where(x => x.CompanyId == c && x.UserId == current.UserId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
                       ?? trip?.DriverId;

        StoredFile? receipt;
        try
        {
            receipt = await files.SaveAsync(form.Receipt, c, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "ValidationError", message = ex.Message });
        }

        var quantity = form.Quantity is > 0 ? form.Quantity.Value : 1;
        var cost = new TransportCost
        {
            Id = Guid.NewGuid(),
            CompanyId = c,
            TransportOrderId = trip?.TransportOrderId,
            TripId = trip?.Id,
            Category = category,
            Source = TransportCostSource.Manual,
            ReferenceType = trip is null ? null : nameof(Trip),
            ReferenceId = trip?.Id,
            Quantity = quantity,
            Unit = category == TransportCostCategory.Fuel ? "l" : "kom",
            UnitPrice = form.UnitPrice is > 0 ? form.UnitPrice.Value : form.Amount.Value / quantity,
            Amount = form.Amount.Value,
            Currency = "EUR",
            IsEstimated = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Notes = Clean(form.Notes),
            Status = TransportCostStatus.Pending,
            IncurredAt = form.IncurredAt ?? DateTimeOffset.UtcNow,
            Location = Clean(form.Location),
            CountryCode = Clean(form.CountryCode)?.ToUpperInvariant(),
            PaymentMethod = Clean(form.PaymentMethod),
            CardLabel = form.PaymentMethod is "cash" ? null : Clean(form.CardLabel),
            OdometerKm = form.OdometerKm,
            VehicleId = form.VehicleId ?? trip?.VehicleId,
            DriverId = driverId,
            ReceiptFileId = receipt?.Id,
            CreatedByUserId = current.UserId,
        };
        db.TransportCosts.Add(cost);

        if (cost.VehicleId is { } costVehicle && form.OdometerKm is { } km)
        {
            var vehicle = await db.Vehicles.SingleAsync(x => x.Id == costVehicle, ct);
            if (vehicle.OdometerKm is null || km > vehicle.OdometerKm) vehicle.OdometerKm = km;
        }

        audit.Add("Cost.Create", nameof(TransportCost), cost.Id, new { Category = cost.Category.ToString(), cost.Amount, cost.Currency, cost.Location });
        await db.SaveChangesAsync(ct);
        return Ok(new { cost.Id });
    }

    [HttpPost("costs/{id:guid}/status")]
    [Authorize(Roles = "Administrator,Dispatcher")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] CostStatusRequest request, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        if (!Enum.TryParse<TransportCostStatus>(request.Status, true, out var status))
            return BadRequest(new { code = "ValidationError", message = "Status nije ispravan." });

        var cost = await db.TransportCosts.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == id, ct);
        if (cost is null) return NotFound();

        var old = cost.Status.ToString();
        cost.Status = status;
        audit.Add("Cost.Status", nameof(TransportCost), cost.Id, new { Status = status.ToString() }, new { Status = old });
        await db.SaveChangesAsync(ct);
        return Ok(new { cost.Id, Status = status.ToString() });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class CostForm
{
    public string? Category { get; set; }
    public Guid? TripId { get; set; }
    public decimal? Amount { get; set; }
    public DateTimeOffset? IncurredAt { get; set; }
    public string? Location { get; set; }
    public string? CountryCode { get; set; }
    public string? PaymentMethod { get; set; }
    public string? CardLabel { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public int? OdometerKm { get; set; }
    public Guid? VehicleId { get; set; }
    public string? Notes { get; set; }
    public IFormFile? Receipt { get; set; }
}

public sealed record CostStatusRequest(string Status);
