using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>A chat message as the app and the web page show it.</summary>
public sealed record ChatMessageDto(Guid Id, Guid DriverId, bool FromDriver, string? SenderName, string Text, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>A driver's conversation in the dispatcher's list.</summary>
public sealed record ChatThreadDto(Guid DriverId, string DriverName, string? LastText, DateTimeOffset? LastAt, bool LastFromDriver, int Unread);

/// <summary>
/// Chat between drivers and dispatch. Stored in driver_chat_messages and pushed live over
/// SignalR (NavigationHub): "chatMessage" to the driver's group (the phone) and to the
/// company group (dispatchers on the web).
/// </summary>
public sealed class DriverChatService(ProMapCargoDbContext db, ICurrentUserContext current, IHubContext<NavigationHub> hub)
{
    private const int MaxLength = 2000;

    public async Task<IReadOnlyList<ChatMessageDto>> ConversationAsync(Guid companyId, Guid driverId, int take, CancellationToken ct)
        => (await db.DriverChatMessages.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.DriverId == driverId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(take)
                .ToListAsync(ct))
            .OrderBy(x => x.CreatedAt)
            .Select(ToDto)
            .ToList();

    public async Task<ChatMessageDto> SendAsync(Guid companyId, Guid driverId, bool fromDriver, string text, Guid? tripId, CancellationToken ct)
    {
        var clean = text.Trim();
        if (clean.Length > MaxLength) clean = clean[..MaxLength];

        var senderName = current.UserId is { } userId
            ? await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        var message = new DriverChatMessage
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            DriverId = driverId,
            SenderUserId = current.UserId,
            SenderName = senderName,
            FromDriver = fromDriver,
            Text = clean,
            TripId = tripId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.DriverChatMessages.Add(message);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(message);
        await hub.Clients.Groups($"driver:{driverId}", $"company:{companyId}").SendAsync("chatMessage", dto, ct);
        return dto;
    }

    /// <summary>Marks the other side's messages as read (the driver reads dispatch, dispatch reads the driver).</summary>
    public async Task MarkReadAsync(Guid companyId, Guid driverId, bool readerIsDriver, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.DriverChatMessages
            .Where(x => x.CompanyId == companyId && x.DriverId == driverId && x.ReadAt == null && x.FromDriver != readerIsDriver)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, now), ct);
    }

    public async Task<IReadOnlyList<ChatThreadDto>> ThreadsAsync(Guid companyId, CancellationToken ct)
    {
        var drivers = await db.Drivers.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { x.Id, x.FullName })
            .ToListAsync(ct);
        var last = await db.DriverChatMessages.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.DriverId)
            .Select(g => new
            {
                DriverId = g.Key,
                Last = g.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Text, x.CreatedAt, x.FromDriver }).First(),
                Unread = g.Count(x => x.FromDriver && x.ReadAt == null),
            })
            .ToListAsync(ct);
        var byDriver = last.ToDictionary(x => x.DriverId);

        return drivers
            .Select(d => byDriver.TryGetValue(d.Id, out var t)
                ? new ChatThreadDto(d.Id, d.FullName, t.Last.Text, t.Last.CreatedAt, t.Last.FromDriver, t.Unread)
                : new ChatThreadDto(d.Id, d.FullName, null, null, false, 0))
            .OrderByDescending(x => x.Unread > 0)
            .ThenByDescending(x => x.LastAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.DriverName)
            .ToList();
    }

    private static ChatMessageDto ToDto(DriverChatMessage x)
        => new(x.Id, x.DriverId, x.FromDriver, x.SenderName, x.Text, x.CreatedAt, x.ReadAt);
}
