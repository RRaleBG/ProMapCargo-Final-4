namespace ProMapCargo.Api.Models;

/// <summary>
/// A chat message between a driver (mobile app, Poruke) and dispatch (web, Poruke).
/// Table: driver_chat_messages (Sql/09-driver-chat.sql for existing databases).
/// </summary>
public sealed class DriverChatMessage
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DriverId { get; set; }
    /// <summary>The account that wrote it (driver or dispatcher).</summary>
    public Guid? SenderUserId { get; set; }
    public string? SenderName { get; set; }
    public bool FromDriver { get; set; }
    public string Text { get; set; } = "";
    public Guid? TripId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>When the other side read it (null: unread).</summary>
    public DateTimeOffset? ReadAt { get; set; }
}
