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
public sealed class FinanceController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit, FileStorageService files, FinanceQueries queries) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<object>> Overview([FromQuery] Guid? driverId, [FromQuery] string mode = "driver", CancellationToken ct = default)
    {
        var data = await queries.OverviewAsync(driverId, mode, ct);
        return data is null ? Unauthorized() : Ok(data);
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
