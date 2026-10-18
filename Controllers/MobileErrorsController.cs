using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>
/// Errors reported by the mobile app (crashes, failed actions, "Pošalji izveštaj").
/// Kept in the audit log (Action "Mobile.Error"), so no new table is needed; the admin
/// page /mobile-errors lists them. Open to phones that are not signed in yet (an error
/// before sign-in matters too); those are stored without a company.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/mobile/errors")]
public sealed class MobileErrorsController(ProMapCargoDbContext db, ICurrentUserContext current) : ControllerBase
{
    public const string AuditAction = "Mobile.Error";
    private const int MaxReportsPerRequest = 50;

    [HttpPost]
    [RequestSizeLimit(512 * 1024)]
    public async Task<IActionResult> Post([FromBody] List<MobileErrorReport> reports, CancellationToken ct)
    {
        if (reports is not { Count: > 0 })
        {
            return BadRequest(new { code = "ValidationError", message = "Nema izveštaja." });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        foreach (var report in reports.Take(MaxReportsPerRequest))
        {
            var clean = report with
            {
                DeviceId = Cut(report.DeviceId, 80) ?? "nepoznat",
                Device = Cut(report.Device, 120),
                OsVersion = Cut(report.OsVersion, 40),
                AppVersion = Cut(report.AppVersion, 40),
                Kind = Cut(report.Kind, 20) ?? "error",
                Screen = Cut(report.Screen, 120),
                Message = Cut(report.Message, 1000) ?? "(bez poruke)",
                Details = Cut(report.Details, 8000),
            };

            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CompanyId = current.CompanyId ?? Guid.Empty,
                UserId = current.UserId,
                CreatedAt = clean.OccurredAt is { } at && at < DateTimeOffset.UtcNow.AddMinutes(5) ? at : DateTimeOffset.UtcNow,
                Action = AuditAction,
                EntityType = "MobileDevice",
                EntityId = clean.DeviceId!,
                NewValueJson = JsonSerializer.Serialize(clean),
                IpAddress = ip,
                Source = "Mobile",
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { received = Math.Min(reports.Count, MaxReportsPerRequest) });
    }

    private static string? Cut(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value.Trim() : value[..max];
}

/// <summary>One error from a phone.</summary>
public sealed record MobileErrorReport(
    string? DeviceId,
    string? Device,
    string? OsVersion,
    string? AppVersion,
    string? Kind,
    string? Screen,
    string? Message,
    string? Details,
    DateTimeOffset? OccurredAt);
