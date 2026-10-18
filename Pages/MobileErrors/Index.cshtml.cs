using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Controllers;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.MobileErrors;

/// <summary>
/// Administrator view of the errors phones reported: which phone (device, user, app version)
/// and what went wrong, newest first, with a per-phone count of the last 7 days.
/// </summary>
[Authorize(Roles = "Administrator")]
public sealed class IndexModel(ProMapCargoDbContext db, ICurrentUserContext current) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [BindProperty(SupportsGet = true)]
    public string? Device { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    public IReadOnlyList<Row> Rows { get; private set; } = [];

    public IReadOnlyList<DeviceSummary> Devices { get; private set; } = [];

    public int Last24Hours { get; private set; }

    public sealed record Row(DateTimeOffset At, string DeviceId, string Device, string? User, string? AppVersion, string? OsVersion, string Kind, string? Screen, string Message, string? Details, string? Ip);

    public sealed record DeviceSummary(string DeviceId, string Device, string? User, int Count, DateTimeOffset Last);

    public async Task OnGetAsync(CancellationToken ct)
    {
        // The company's phones, and phones that reported before anyone signed in.
        var company = current.CompanyId ?? Guid.Empty;
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(x => x.Action == MobileErrorsController.AuditAction && x.CreatedAt >= since &&
                        (x.CompanyId == company || x.CompanyId == Guid.Empty))
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        var userIds = logs.Where(x => x.UserId != null).Select(x => x.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName ?? x.Email ?? x.Id.ToString(), ct);

        var rows = logs.Select(log =>
        {
            MobileErrorReport? report = null;
            try
            {
                report = log.NewValueJson is null ? null : JsonSerializer.Deserialize<MobileErrorReport>(log.NewValueJson, JsonOptions);
            }
            catch (JsonException)
            {
            }

            return new Row(
                log.CreatedAt,
                log.EntityId,
                report?.Device ?? log.EntityId,
                log.UserId is { } id && users.TryGetValue(id, out var name) ? name : null,
                report?.AppVersion,
                report?.OsVersion,
                report?.Kind ?? "error",
                report?.Screen,
                report?.Message ?? "(bez poruke)",
                report?.Details,
                log.IpAddress);
        }).ToList();

        var week = DateTimeOffset.UtcNow.AddDays(-7);
        Devices = rows.Where(x => x.At >= week)
            .GroupBy(x => x.DeviceId)
            .Select(g => new DeviceSummary(g.Key, g.First().Device, g.Select(x => x.User).FirstOrDefault(u => u != null), g.Count(), g.Max(x => x.At)))
            .OrderByDescending(x => x.Count)
            .ToList();
        Last24Hours = rows.Count(x => x.At >= DateTimeOffset.UtcNow.AddDays(-1));

        Rows = rows
            .Where(x => string.IsNullOrWhiteSpace(Device) || x.DeviceId == Device)
            .Where(x => string.IsNullOrWhiteSpace(Kind) || string.Equals(x.Kind, Kind, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string KindLabel(string kind) => kind switch
    {
        "crash" => "Pad aplikacije",
        "report" => "Izveštaj vozača",
        _ => "Greška",
    };
}
