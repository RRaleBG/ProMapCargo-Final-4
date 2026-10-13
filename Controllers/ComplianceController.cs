using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>
/// /compliance: vehicle and trailer documents with their validity, service, repairs,
/// faults, notes and trailer changes. A vehicle is blocked when a mandatory document is
/// missing or expired, expiring when one runs out within 30 days.
/// </summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/compliance")]
public sealed class ComplianceController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit, FileStorageService files, ComplianceQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Overview(CancellationToken ct)
    {
        var data = await queries.OverviewAsync(ct);
        return data is null ? Unauthorized() : Ok(data);
    }

    [HttpPost("vehicles/{vehicleId:guid}/documents")]
    [RequestSizeLimit(FileStorageService.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<object>> AddDocument(Guid vehicleId, [FromForm] DocumentForm form, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        if (!await db.Vehicles.AnyAsync(x => x.CompanyId == c && x.Id == vehicleId, ct)) return NotFound();
        if (form.DocumentType is null || !ComplianceQueries.DocumentTypes.ContainsKey(form.DocumentType))
            return BadRequest(new { code = "ValidationError", message = "Izaberite tip dokumenta." });
        if (form.ExpiryDate is null)
            return BadRequest(new { code = "ValidationError", message = "Unesite datum važenja dokumenta." });
        if (form.IssueDate is { } issued && issued > form.ExpiryDate)
            return BadRequest(new { code = "ValidationError", message = "Datum izdavanja ne može biti posle datuma važenja." });

        StoredFile? file;
        try
        {
            file = await files.SaveAsync(form.File, c, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "ValidationError", message = ex.Message });
        }

        var document = new ComplianceDocument
        {
            Id = Guid.NewGuid(),
            CompanyId = c,
            VehicleId = vehicleId,
            DocumentType = form.DocumentType,
            Number = Clean(form.Number),
            IssueDate = form.IssueDate,
            ExpiryDate = form.ExpiryDate.Value,
            Issuer = Clean(form.Issuer),
            Note = Clean(form.Note),
            FileId = file?.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = current.UserId,
        };
        db.ComplianceDocuments.Add(document);
        audit.Add("Compliance.Document", nameof(Vehicle), vehicleId, new { document.DocumentType, document.Number, document.ExpiryDate });
        await db.SaveChangesAsync(ct);
        return Ok(new { document.Id });
    }

    [HttpPost("vehicles/{vehicleId:guid}/log")]
    public async Task<ActionResult<object>> AddLog(Guid vehicleId, [FromBody] LogEntryRequest request, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == vehicleId, ct);
        if (vehicle is null) return NotFound();
        if (!Enum.TryParse<VehicleLogKind>(request.Kind, true, out var kind))
            return BadRequest(new { code = "ValidationError", message = "Vrsta unosa nije ispravna." });
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { code = "ValidationError", message = "Naslov je obavezan." });
        if (request.Cost is < 0 || request.OdometerKm is < 0)
            return BadRequest(new { code = "ValidationError", message = "Trošak i kilometraža ne mogu biti negativni." });

        // Faults and repairs start open unless reported as done; service and notes are done.
        var open = kind is VehicleLogKind.Fault or VehicleLogKind.Repair && !request.Completed;
        var entry = new VehicleLogEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = c,
            VehicleId = vehicleId,
            Kind = kind,
            Title = request.Title.Trim(),
            Description = Clean(request.Description),
            OccurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow,
            OdometerKm = request.OdometerKm,
            Cost = request.Cost,
            Currency = "EUR",
            Status = open ? VehicleLogStatus.Open : VehicleLogStatus.Completed,
            Severity = kind == VehicleLogKind.Fault ? (request.Severity is "Critical" or "Warning" ? request.Severity : "Warning") : "Info",
            Workshop = Clean(request.Workshop),
            ResolvedAt = open ? null : request.OccurredAt ?? DateTimeOffset.UtcNow,
            CreatedByName = User.FindFirst("display_name")?.Value ?? User.Identity?.Name,
            CreatedByUserId = current.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.VehicleLogEntries.Add(entry);

        if (request.OdometerKm is { } km && (vehicle.OdometerKm is null || km > vehicle.OdometerKm))
        {
            vehicle.OdometerKm = km;
        }

        audit.Add($"Compliance.{kind}", nameof(Vehicle), vehicleId, new { entry.Title, entry.Cost, Status = entry.Status.ToString() });
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id });
    }

    [HttpPost("log/{entryId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid entryId, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var entry = await db.VehicleLogEntries.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == entryId, ct);
        if (entry is null) return NotFound();

        entry.Status = VehicleLogStatus.Completed;
        entry.ResolvedAt = DateTimeOffset.UtcNow;
        audit.Add("Compliance.Resolve", nameof(Vehicle), entry.VehicleId, new { entry.Title });
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id });
    }

    [HttpPost("vehicles/{vehicleId:guid}/trailer")]
    public async Task<IActionResult> ChangeTrailer(Guid vehicleId, [FromBody] TrailerRequest request, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == vehicleId, ct);
        if (vehicle is null) return NotFound();
        if (request.TrailerId is { } trailerId && !await db.Vehicles.AnyAsync(x => x.CompanyId == c && x.Id == trailerId && x.Type == VehicleType.Trailer, ct))
            return BadRequest(new { code = "ValidationError", message = "Izabrana prikolica ne postoji." });
        if (request.TrailerId is { } taken && await db.Vehicles.AnyAsync(x => x.CompanyId == c && x.Id != vehicleId && x.TrailerId == taken, ct))
            return Conflict(new { code = "Conflict", message = "Prikolica je već povezana sa drugim vozilom." });

        var old = vehicle.TrailerId;
        vehicle.TrailerId = request.TrailerId;
        db.TrailerAssignments.Add(new TrailerAssignment
        {
            Id = Guid.NewGuid(), CompanyId = c, VehicleId = vehicleId, TrailerId = request.TrailerId,
            AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = current.UserId,
        });
        audit.Add("Compliance.Trailer", nameof(Vehicle), vehicleId, new { TrailerId = request.TrailerId }, new { TrailerId = old });
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DocumentForm
{
    public string? DocumentType { get; set; }
    public string? Number { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Issuer { get; set; }
    public string? Note { get; set; }
    public IFormFile? File { get; set; }
}

public sealed record LogEntryRequest(
    string? Kind,
    string? Title,
    string? Description,
    DateTimeOffset? OccurredAt,
    int? OdometerKm,
    decimal? Cost,
    string? Severity,
    string? Workshop,
    bool Completed);

public sealed record TrailerRequest(Guid? TrailerId);
