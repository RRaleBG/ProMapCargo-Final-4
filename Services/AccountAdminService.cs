using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public interface IAccountAdminService
{
    Task<AccountAdminDashboard> GetDashboardAsync(AccountAdminQuery query, CancellationToken cancellationToken);
    Task<AccountAdminOperationResult> ToggleActiveAsync(Guid targetUserId, CancellationToken cancellationToken);
    Task<AccountAdminOperationResult> ToggleLockAsync(Guid targetUserId, CancellationToken cancellationToken);
    Task<AccountAdminOperationResult> SetRoleAsync(Guid targetUserId, string roleName, bool assign, CancellationToken cancellationToken);
    Task<AccountAdminOperationResult> TogglePermissionAsync(Guid roleId, string permission, bool grant, CancellationToken cancellationToken);
}

public sealed class AccountAdminService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ProMapCargoDbContext db,
    ICurrentUserContext currentUser,
    UserPresenceTracker presenceTracker,
    IIPAddressLocationService ipAddressLocationService,
    ILogger<AccountAdminService> logger) : IAccountAdminService
{
    public static readonly IReadOnlyList<string> PermissionCatalog =
    [
        "users.read",
        "users.manage",
        "roles.manage",
        "permissions.manage",
        "dispatch.manage",
        "trips.manage",
        "drivers.manage",
        "vehicles.manage",
        "orders.manage",
        "navigation.live",
        "alerts.manage",
        "compliance.manage",
        "finance.manage",
        "audit.read",
        "settings.manage"
    ];

    public async Task<AccountAdminDashboard> GetDashboardAsync(AccountAdminQuery query, CancellationToken cancellationToken)
    {
        var normalizedQuery = query with
        {
            Search = query.Search?.Trim(),
            Status = string.IsNullOrWhiteSpace(query.Status) ? "all" : query.Status.Trim().ToLowerInvariant(),
            Role = string.IsNullOrWhiteSpace(query.Role) ? "all" : query.Role.Trim(),
            Page = query.Page <= 0 ? 1 : query.Page,
            PageSize = query.PageSize is < 5 or > 100 ? 20 : query.PageSize
        };

        var users = await userManager.Users
            .OrderBy(x => x.Email)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roles = await roleManager.Roles
            .OrderBy(x => x.Name)
            .Select(x => new AccountAdminRoleRow(x.Id, x.Name ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rolePermissions = await db.RolePermissions
            .AsNoTracking()
            .GroupBy(x => x.RoleId)
            .ToDictionaryAsync(
                x => x.Key,
                x => x.Select(v => v.Permission).ToHashSet(StringComparer.OrdinalIgnoreCase),
                cancellationToken)
            .ConfigureAwait(false);

        var presenceLogs = await db.AuditLogs
            .AsNoTracking()
            .Where(x => x.EntityType == "ApplicationUser" && x.Action.StartsWith("Presence."))
            .Select(x => new { x.EntityId, x.Action, x.CreatedAt, x.IpAddress })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var presenceByUser = presenceLogs
            .GroupBy(x => x.EntityId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var lastConnected = g
                        .Where(x => x.Action == "Presence.Connected")
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();

                    var lastDisconnected = g
                        .Where(x => x.Action == "Presence.Disconnected")
                        .OrderByDescending(x => x.CreatedAt)
                        .FirstOrDefault();

                    var lastEvent = g.OrderByDescending(x => x.CreatedAt).FirstOrDefault();

                    return new
                    {
                        LastSeenAt = lastEvent?.CreatedAt,
                        LastConnectedAt = lastConnected?.CreatedAt,
                        LastDisconnectedAt = lastDisconnected?.CreatedAt,
                        LastIpAddress = !string.IsNullOrWhiteSpace(lastConnected?.IpAddress)
                            ? lastConnected!.IpAddress
                            : lastEvent?.IpAddress
                    };
                },
                StringComparer.OrdinalIgnoreCase);

        var userRows = new List<AccountAdminUserRow>(users.Count);

        foreach (var user in users)
        {
            var userRoles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
            var isLocked = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;
            presenceByUser.TryGetValue(user.Id.ToString(), out var presence);

            var location = await ipAddressLocationService
                .ResolveAsync(presence?.LastIpAddress, cancellationToken)
                .ConfigureAwait(false);

            userRows.Add(new AccountAdminUserRow
            {
                Id = user.Id,
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (user.UserName ?? user.Email ?? user.Id.ToString()) : user.DisplayName,
                Email = user.Email ?? user.UserName ?? string.Empty,
                IsActive = user.IsActive,
                IsLocked = isLocked,
                IsOnline = presenceTracker.IsOnline(user.Id),
                LastSeenAt = presence?.LastSeenAt,
                LastConnectedAt = presence?.LastConnectedAt,
                LastDisconnectedAt = presence?.LastDisconnectedAt,
                LastIpAddress = presence?.LastIpAddress,
                LastLocation = FormatLocation(location),
                Roles = userRoles.OrderBy(x => x).ToList()
            });
        }

        IEnumerable<AccountAdminUserRow> filtered = userRows;

        if (!string.IsNullOrWhiteSpace(normalizedQuery.Search))
        {
            var term = normalizedQuery.Search;
            filtered = filtered.Where(u =>
                u.DisplayName.Contains(term!, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(term!, StringComparison.OrdinalIgnoreCase));
        }

        filtered = normalizedQuery.Status switch
        {
            "active" => filtered.Where(x => x.IsActive),
            "inactive" => filtered.Where(x => !x.IsActive),
            "locked" => filtered.Where(x => x.IsLocked),
            "online" => filtered.Where(x => x.IsOnline),
            _ => filtered
        };

        if (!string.Equals(normalizedQuery.Role, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(x => x.Roles.Contains(normalizedQuery.Role, StringComparer.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        var totalFiltered = filteredList.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalFiltered / (double)normalizedQuery.PageSize));
        var page = Math.Min(normalizedQuery.Page, totalPages);

        var pageUsers = filteredList
            .Skip((page - 1) * normalizedQuery.PageSize)
            .Take(normalizedQuery.PageSize)
            .ToList();

        var auditLogs = await db.AuditLogs
            .AsNoTracking()
            .Where(x => x.Action.StartsWith("User.") || x.Action.StartsWith("Role."))
            .OrderByDescending(x => x.CreatedAt)
            .Take(30)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AccountAdminDashboard
        {
            Summary = new AccountAdminSummary
            {
                TotalUsers = userRows.Count,
                ActiveUsers = userRows.Count(x => x.IsActive),
                OnlineUsers = userRows.Count(x => x.IsOnline),
                RolesCount = roles.Count
            },
            Users = pageUsers,
            AvailableRoles = roles,
            RolePermissions = roles
                .Select(role => new AccountAdminRolePermissionRow
                {
                    RoleId = role.Id,
                    RoleName = role.Name,
                    AssignedPermissions = rolePermissions.TryGetValue(role.Id, out var assigned)
                        ? assigned.ToList()
                        : [],
                    AllPermissions = PermissionCatalog
                })
                .ToList(),
            RecentAuditLogs = auditLogs,
            Pager = new AccountAdminPager
            {
                Page = page,
                PageSize = normalizedQuery.PageSize,
                TotalItems = totalFiltered,
                TotalPages = totalPages
            }
        };
    }

    public async Task<AccountAdminOperationResult> ToggleActiveAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(x => x.Id == targetUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AccountAdminOperationResult.Fail("Korisnik nije pronađen.");
        }

        if (currentUser.UserId == user.Id && user.IsActive)
        {
            return AccountAdminOperationResult.Fail("Ne možete deaktivirati sopstveni nalog.");
        }

        user.IsActive = !user.IsActive;
        var result = await userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return AccountAdminOperationResult.Fail(string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        await WriteAuditAsync("User.ToggleActive", "ApplicationUser", user.Id.ToString(), new { user.IsActive }, cancellationToken).ConfigureAwait(false);
        return AccountAdminOperationResult.Ok(user.IsActive ? "Korisnik je aktiviran." : "Korisnik je deaktiviran.");
    }

    public async Task<AccountAdminOperationResult> ToggleLockAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(x => x.Id == targetUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AccountAdminOperationResult.Fail("Korisnik nije pronađen.");
        }

        if (currentUser.UserId == user.Id)
        {
            return AccountAdminOperationResult.Fail("Ne možete zaključati sopstveni nalog.");
        }

        var currentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;
        var lockResult = await userManager.SetLockoutEndDateAsync(
            user,
            currentlyLocked ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddYears(10)).ConfigureAwait(false);

        if (!lockResult.Succeeded)
        {
            return AccountAdminOperationResult.Fail(string.Join("; ", lockResult.Errors.Select(x => x.Description)));
        }

        await WriteAuditAsync("User.ToggleLock", "ApplicationUser", user.Id.ToString(), new { Locked = !currentlyLocked }, cancellationToken).ConfigureAwait(false);
        return AccountAdminOperationResult.Ok(currentlyLocked ? "Korisnik je otključan." : "Korisnik je zaključan.");
    }

    public async Task<AccountAdminOperationResult> SetRoleAsync(Guid targetUserId, string roleName, bool assign, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return AccountAdminOperationResult.Fail("Rola nije validna.");
        }

        var user = await userManager.Users.SingleOrDefaultAsync(x => x.Id == targetUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return AccountAdminOperationResult.Fail("Korisnik nije pronađen.");
        }

        if (!await roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
        {
            return AccountAdminOperationResult.Fail("Rola ne postoji.");
        }

        if (currentUser.UserId == user.Id && string.Equals(roleName, "Administrator", StringComparison.OrdinalIgnoreCase) && !assign)
        {
            return AccountAdminOperationResult.Fail("Ne možete ukloniti Administrator rolu sa sopstvenog naloga.");
        }

        var result = assign
            ? await userManager.AddToRoleAsync(user, roleName).ConfigureAwait(false)
            : await userManager.RemoveFromRoleAsync(user, roleName).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return AccountAdminOperationResult.Fail(string.Join("; ", result.Errors.Select(x => x.Description)));
        }

        await WriteAuditAsync("User.SetRole", "ApplicationUser", user.Id.ToString(), new { roleName, assign }, cancellationToken).ConfigureAwait(false);
        return AccountAdminOperationResult.Ok(assign ? "Rola je dodeljena." : "Rola je uklonjena.");
    }

    public async Task<AccountAdminOperationResult> TogglePermissionAsync(Guid roleId, string permission, bool grant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return AccountAdminOperationResult.Fail("Dozvola nije validna.");
        }

        var role = await roleManager.Roles.SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return AccountAdminOperationResult.Fail("Rola nije pronađena.");
        }

        var existing = await db.RolePermissions.SingleOrDefaultAsync(
            x => x.RoleId == roleId && x.Permission == permission,
            cancellationToken).ConfigureAwait(false);

        if (grant && existing is null)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = permission });
        }

        if (!grant && existing is not null)
        {
            db.RolePermissions.Remove(existing);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync("Role.TogglePermission", "ApplicationRole", roleId.ToString(), new { permission, grant }, cancellationToken).ConfigureAwait(false);

        return AccountAdminOperationResult.Ok(grant ? "Dozvola je dodeljena roli." : "Dozvola je uklonjena sa role.");
    }

    private static string? FormatLocation(IpAddressLocation? location)
    {
        if (location is null)
        {
            return null;
        }

        var parts = new[] { location.City, location.Region, location.Country }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    private async Task WriteAuditAsync(string action, string entityType, string entityId, object payload, CancellationToken cancellationToken)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            CompanyId = currentUser.CompanyId ?? Guid.Empty,
            UserId = currentUser.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            NewValueJson = JsonSerializer.Serialize(payload),
            IpAddress = null,
            Source = "Web"
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Audit write failed for {Action} {EntityType} {EntityId}", action, entityType, entityId);
            throw;
        }
    }
}

public sealed record AccountAdminQuery(string? Search, string Status, string Role, int Page, int PageSize);

public sealed class AccountAdminDashboard
{
    public AccountAdminSummary Summary { get; set; } = new();
    public IReadOnlyList<AccountAdminUserRow> Users { get; set; } = [];
    public IReadOnlyList<AccountAdminRoleRow> AvailableRoles { get; set; } = [];
    public IReadOnlyList<AccountAdminRolePermissionRow> RolePermissions { get; set; } = [];
    public IReadOnlyList<AuditLog> RecentAuditLogs { get; set; } = [];
    public AccountAdminPager Pager { get; set; } = new();
}

public sealed class AccountAdminSummary
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int OnlineUsers { get; set; }
    public int RolesCount { get; set; }
}

public sealed class AccountAdminUserRow
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsLocked { get; set; }
    public bool IsOnline { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastDisconnectedAt { get; set; }
    public string? LastIpAddress { get; set; }
    public string? LastLocation { get; set; }
    public List<string> Roles { get; set; } = [];
}

public sealed record AccountAdminRoleRow(Guid Id, string Name);

public sealed class AccountAdminRolePermissionRow
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public IReadOnlyList<string> AssignedPermissions { get; set; } = [];
    public IReadOnlyList<string> AllPermissions { get; set; } = [];
}

public sealed class AccountAdminPager
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public sealed record AccountAdminOperationResult(bool Success, string Message)
{
    public static AccountAdminOperationResult Ok(string message) => new(true, message);
    public static AccountAdminOperationResult Fail(string message) => new(false, message);
}
