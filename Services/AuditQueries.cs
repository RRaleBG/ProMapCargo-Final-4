using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;

namespace ProMapCargo.Api.Services;

public sealed record AuditRow(
    Guid Id,
    DateTimeOffset CreatedAt,
    string User,
    string Action,
    string EntityType,
    string EntityId,
    string? OldValueJson,
    string? NewValueJson,
    string? IpAddress,
    string Source);

/// <summary>Data of /audit: the change log, newest first, with optional filters.</summary>
public sealed class AuditQueries(ProMapCargoDbContext db, ICurrentUserContext current)
{
    /// <summary>Null when the request has no company context.</summary>
    public async Task<List<AuditRow>?> ListAsync(string? user, string? action, string? entity, DateOnly? from, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;

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

        return items.Select(x => new AuditRow(
            x.Id,
            x.CreatedAt,
            x.UserId is { } u && emails.TryGetValue(u, out var email) ? email : "sistem",
            x.Action,
            x.EntityType,
            x.EntityId,
            x.OldValueJson,
            x.NewValueJson,
            x.IpAddress,
            x.Source)).ToList();
    }
}
