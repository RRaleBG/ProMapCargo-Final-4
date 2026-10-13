using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>
/// Data of /compliance: vehicle and trailer documents with their validity, service, repairs,
/// faults, notes and trailer changes. A vehicle is blocked when a mandatory document is
/// missing or expired, expiring when one runs out within 30 days.
/// </summary>
public sealed class ComplianceQueries(ProMapCargoDbContext db, ICurrentUserContext current)
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

    /// <summary>Null when the request has no company context.</summary>
    public async Task<object?> OverviewAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;

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

        return (new
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
}
