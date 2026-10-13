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
public sealed class ComplianceController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit, FileStorageService files) : ControllerBase
{
    public const int ExpiringDays = 30;

    // Document types of the "Novi dokument" form, with their Serbian names.
    public static readonly Dictionary<string, string> DocumentTypes = new()
    {
        ["registration"] = "Saobraćajna dozvola",
        ["euro-truck"] = "Euro potvrda · kamion",
        ["euro-trailer"] = "Euro potvrda · prikolica",
        ["technical"] = "Tehnički pregled",
        ["tir"] = "TIR sertifikat",
        ["license"] = "Licenca / dozvola Republike Srbije",
        ["tachograph"] = "Tahograf",
        ["fire"] = "PP aparat / sertifikat",
    };

    private static readonly string[] MandatoryTruck = ["registration", "technical", "euro-truck", "tachograph"];
    private static readonly string[] MandatoryTrailer = ["registration", "technical", "euro-trailer"];

    [HttpGet]
    public async Task<ActionResult<object>> Overview(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == c).OrderBy(x => x.Type == VehicleType.Trailer).ThenBy(x => x.Registration).ToListAsync(ct);
        var documents = await db.ComplianceDocuments.AsNoTracking().Where(x => x.CompanyId == c).ToListAsync(ct);
        var log = await db.VehicleLogEntries.AsNoTracking().Where(x => x.CompanyId == c).OrderByDescending(x => x.OccurredAt).ToListAsync(ct);
        var assignments = await db.TrailerAssignments.AsNoTracking().Where(x => x.CompanyId == c).OrderByDescending(x => x.AssignedAt).ToListAsync(ct);
        var ids = vehicles.Select(v => v.Id.ToString()).ToList();
        var history = await db.AuditLogs.AsNoTracking()
            .Where(x => x.CompanyId == c && ids.Contains(x.EntityId))
            .OrderByDescending(x => x.CreatedAt)
            .Take(1000)
            .ToListAsync(ct);
        var userIds = history.Where(x => x.UserId != null).Select(x => x.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName.Length > 0 ? x.DisplayName : x.Email ?? "", ct);

        string State(DateOnly? expiry) => expiry is null ? "missing"
            : expiry < today ? "expired"
            : expiry <= today.AddDays(ExpiringDays) ? "expiring"
            : "valid";

        // Newest document of each type; the vehicle record's own dates fill the gaps.
        DateOnly? Expiry(Vehicle v, string type)
        {
            var document = documents.Where(d => d.VehicleId == v.Id && d.DocumentType == type).MaxBy(d => d.ExpiryDate);
            return document?.ExpiryDate ?? type switch
            {
                "registration" => v.RegistrationExpiry,
                "technical" => v.TechnicalInspectionExpiry,
                "tir" => v.TirExpiry,
                "euro-truck" or "euro-trailer" => v.EuroConfirmationExpiry,
                "tachograph" => v.TachographCalibrationExpiry,
                _ => null,
            };
        }

        // TIR counts only for vehicles that carry one.
        string TirState(Vehicle v) => v.TirExpiry is not null || documents.Any(d => d.VehicleId == v.Id && d.DocumentType == "tir") ? State(Expiry(v, "tir")) : "none";

        (string Status, int Critical, int Expiring) Assess(Vehicle v)
        {
            var mandatory = v.Type == VehicleType.Trailer ? MandatoryTrailer : MandatoryTruck;
            var states = mandatory.Select(t => State(Expiry(v, t))).Append(TirState(v)).ToList();
            var critical = states.Count(s => s is "expired" or "missing");
            var expiring = states.Count(s => s == "expiring");
            var status = critical > 0 || v.Status == VehicleStatus.OutOfService ? "blocked" : expiring > 0 ? "expiring" : "compliant";
            return (status, critical, expiring);
        }

        object Card(Vehicle v)
        {
            var isTrailer = v.Type == VehicleType.Trailer;
            var (status, critical, expiring) = Assess(v);
            var tir = TirState(v);
            var trailer = vehicles.FirstOrDefault(t => t.Id == v.TrailerId);
            var vehicleLog = log.Where(l => l.VehicleId == v.Id).ToList();

            return new
            {
                v.Id,
                v.Registration,
                v.Name,
                v.Make,
                v.Model,
                v.VIN,
                v.Year,
                Type = isTrailer ? "trailer" : "truck",
                VehicleType = v.Type.ToString(),
                VehicleStatus = v.Status.ToString(),
                v.EmissionClass,
                v.OdometerKm,
                Status = status,
                CriticalCount = critical,
                ExpiringCount = expiring,
                RegistrationDoc = new { Expiry = Expiry(v, "registration"), State = State(Expiry(v, "registration")) },
                Euro = new { Expiry = Expiry(v, isTrailer ? "euro-trailer" : "euro-truck"), State = State(Expiry(v, isTrailer ? "euro-trailer" : "euro-truck")) },
                Tir = new { Expiry = Expiry(v, "tir"), State = tir },
                Tachograph = new { Expiry = Expiry(v, "tachograph"), State = isTrailer ? "none" : State(Expiry(v, "tachograph")) },
                Trailer = trailer is null ? null : new { trailer.Id, trailer.Registration, trailer.Make, trailer.Model, trailer.EmissionClass },
                Documents = documents.Where(d => d.VehicleId == v.Id).OrderBy(d => d.ExpiryDate).Select(d => new
                {
                    d.Id,
                    d.DocumentType,
                    Title = DocumentTypes.GetValueOrDefault(d.DocumentType, d.DocumentType),
                    d.Number,
                    d.IssueDate,
                    d.ExpiryDate,
                    d.Issuer,
                    d.Note,
                    d.FileId,
                    State = State(d.ExpiryDate),
                    DaysLeft = d.ExpiryDate.DayNumber - today.DayNumber,
                }),
                Service = vehicleLog.Where(l => l.Kind is VehicleLogKind.ServiceMinor or VehicleLogKind.ServiceMajor).Select(Entry),
                Repairs = vehicleLog.Where(l => l.Kind == VehicleLogKind.Repair).Select(Entry),
                Faults = vehicleLog.Where(l => l.Kind == VehicleLogKind.Fault).Select(Entry),
                Notes = vehicleLog.Where(l => l.Kind == VehicleLogKind.Note).Select(Entry),
                TrailerHistory = assignments.Where(a => a.VehicleId == v.Id).Select(a => new
                {
                    a.AssignedAt,
                    Trailer = vehicles.FirstOrDefault(t => t.Id == a.TrailerId)?.Registration,
                }),
                History = history.Where(h => h.EntityId == v.Id.ToString()).Select(h => new
                {
                    h.CreatedAt,
                    h.Action,
                    User = h.UserId is { } u && users.TryGetValue(u, out var name) ? name : "sistem",
                    h.NewValueJson,
                }),
            };
        }

        var statuses = vehicles.Where(v => v.Type != VehicleType.Trailer).Select(v => Assess(v).Status).ToList();

        return Ok(new
        {
            Kpi = new
            {
                Total = vehicles.Count(v => v.Type != VehicleType.Trailer),
                Compliant = statuses.Count(s => s == "compliant"),
                Expiring = statuses.Count(s => s == "expiring"),
                Blocked = statuses.Count(s => s == "blocked"),
                Trailers = vehicles.Count(v => v.Type == VehicleType.Trailer),
                Documents = documents.Count,
            },
            DocumentTypes = DocumentTypes.Select(x => new { Value = x.Key, Label = x.Value }),
            Trailers = vehicles.Where(v => v.Type == VehicleType.Trailer).Select(v => new { v.Id, v.Registration, v.Make, v.Model }),
            Vehicles = vehicles.Select(Card),
        });
    }

    private static object Entry(VehicleLogEntry l) => new
    {
        l.Id,
        Kind = l.Kind.ToString(),
        l.Title,
        l.Description,
        l.OccurredAt,
        l.OdometerKm,
        l.Cost,
        l.Currency,
        Status = l.Status.ToString(),
        l.Severity,
        l.Workshop,
        l.ResolvedAt,
        l.CreatedByName,
    };

    [HttpPost("vehicles/{vehicleId:guid}/documents")]
    [RequestSizeLimit(FileStorageService.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<object>> AddDocument(Guid vehicleId, [FromForm] DocumentForm form, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        if (!await db.Vehicles.AnyAsync(x => x.CompanyId == c && x.Id == vehicleId, ct)) return NotFound();
        if (form.DocumentType is null || !DocumentTypes.ContainsKey(form.DocumentType))
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
