using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

[Authorize]
public sealed class NavigationHub(
    ICurrentUserContext current,
    ProMapCargoDbContext db,
    UserPresenceTracker presenceTracker,
    ILogger<NavigationHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (current.UserId is Guid userId)
        {
            presenceTracker.SetOnline(userId, Context.ConnectionId);
            await WritePresenceAuditAsync(userId, "Presence.Connected").ConfigureAwait(false);
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (current.UserId is Guid userId)
        {
            presenceTracker.SetOffline(userId, Context.ConnectionId);
            await WritePresenceAuditAsync(userId, "Presence.Disconnected").ConfigureAwait(false);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    public Task JoinCompany()
    {
        if (!current.CompanyId.HasValue)
        {
            return Task.CompletedTask;
        }

        return Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"company:{current.CompanyId}");
    }

    public async Task JoinTrip(Guid tripId)
    {
        var companyId = current.CompanyId
            ?? throw new HubException("Company context missing.");

        var exists = await db.Trips.AnyAsync(
            x => x.CompanyId == companyId && x.Id == tripId);

        if (!exists)
        {
            throw new HubException("Trip not found.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"trip:{tripId}");
    }

    public async Task JoinDriver(Guid driverId)
    {
        var companyId = current.CompanyId
            ?? throw new HubException("Company context missing.");

        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == driverId);

        if (driver is null)
        {
            throw new HubException("Driver not found.");
        }

        var isSelf = driver.UserId.HasValue && driver.UserId == current.UserId;
        var isDispatcher = current.IsInRole("Administrator")
                           || current.IsInRole("Dispatcher")
                           || current.IsInRole("Moderator");

        if (!isSelf && !isDispatcher)
        {
            throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"driver:{driverId}");
    }

    private async Task WritePresenceAuditAsync(Guid userId, string action)
    {
        try
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CompanyId = current.CompanyId ?? Guid.Empty,
                UserId = userId,
                Action = action,
                EntityType = "ApplicationUser",
                EntityId = userId.ToString(),
                IpAddress = ResolveClientIp(Context.GetHttpContext()),
                Source = "SignalR"
            });

            await db.SaveChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug(
                "Presence audit canceled for {UserId}: {Action}",
                userId,
                action);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to write presence audit for user {UserId} action {Action}",
                userId,
                action);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error writing presence audit for user {UserId} action {Action}",
                userId,
                action);
        }
    }

    private static string? ResolveClientIp(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        var forwarded = httpContext.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var candidate = forwarded
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        var realIp = httpContext.Request.Headers["X-Real-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(realIp))
        {
            return realIp;
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }
}
