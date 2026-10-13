using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>/audit: the change log with filters and CSV export.</summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/audit")]
public sealed class AuditController(ProMapCargoDbContext db, ICurrentUserContext current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? user, [FromQuery] string? action, [FromQuery] string? entity,
        [FromQuery] DateOnly? from, [FromQuery] string? format, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();

        var query = db.AuditLogs.AsNoTracking().Where(x => x.CompanyId == c);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => EF.Functions.ILike(x.Action, $"%{action.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(x => EF.Functions.ILike(x.EntityId, $"%{entity.Trim()}%") || EF.Functions.ILike(x.EntityType, $"%{entity.Trim()}%"));
        if (from is { } day) query = query.Where(x => x.CreatedAt >= new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (!string.IsNullOrWhiteSpace(user))
        {
            var pattern = $"%{user.Trim()}%";
            var userIds = await db.Users.Where(x => EF.Functions.ILike(x.Email ?? "", pattern) || EF.Functions.ILike(x.DisplayName, pattern)).Select(x => x.Id).ToListAsync(ct);
            var system = "sistem".Contains(user.Trim(), StringComparison.OrdinalIgnoreCase) || "system".Contains(user.Trim(), StringComparison.OrdinalIgnoreCase);
            query = query.Where(x => (x.UserId != null && userIds.Contains(x.UserId.Value)) || (system && x.UserId == null));
        }

        var items = await query.OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
        var ids = items.Where(x => x.UserId != null).Select(x => x.UserId!.Value).Distinct().ToList();
        var emails = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Email ?? x.UserName ?? "", ct);

        var rows = items.Select(x => new
        {
            x.Id,
            x.CreatedAt,
            User = x.UserId is { } u && emails.TryGetValue(u, out var email) ? email : "sistem",
            x.Action,
            x.EntityType,
            x.EntityId,
            x.OldValueJson,
            x.NewValueJson,
            x.IpAddress,
            x.Source,
        }).ToList();

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = new StringBuilder("Vreme;Korisnik;Akcija;Entitet;ID;Nova vrednost;IP;Izvor\n");
            foreach (var r in rows)
            {
                csv.AppendLine(string.Join(';', new[] { r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), r.User, r.Action, r.EntityType, r.EntityId, r.NewValueJson ?? "", r.IpAddress ?? "", r.Source }
                    .Select(v => "\"" + v.Replace("\"", "\"\"") + "\"")));
            }

            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", $"audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }

        return Ok(rows);
    }
}
