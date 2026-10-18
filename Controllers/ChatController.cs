using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>
/// Chat between a driver and dispatch.
/// - Driver (mobile app): GET/POST api/chat/me, POST api/chat/me/read.
/// - Dispatch (web): GET api/chat/threads, GET/POST api/chat/drivers/{driverId}, POST .../read.
/// </summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/chat")]
public sealed class ChatController(ProMapCargoDbContext db, ICurrentUserContext current, DriverChatService chat) : ControllerBase
{
    // ---- Driver ----

    [HttpGet("me")]
    public async Task<ActionResult<object>> MyConversation(CancellationToken ct)
    {
        if (await OwnDriverAsync(ct) is not { } me) return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });
        return Ok(new { me.DriverId, Messages = await chat.ConversationAsync(me.CompanyId, me.DriverId, 200, ct) });
    }

    [HttpPost("me")]
    public async Task<ActionResult<ChatMessageDto>> SendToDispatch([FromBody] ChatSendRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest(new { code = "ValidationError", message = "Poruka je prazna." });
        if (await OwnDriverAsync(ct) is not { } me) return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });
        return Ok(await chat.SendAsync(me.CompanyId, me.DriverId, fromDriver: true, request.Text, me.TripId, ct));
    }

    [HttpPost("me/read")]
    public async Task<IActionResult> ReadMine(CancellationToken ct)
    {
        if (await OwnDriverAsync(ct) is not { } me) return NotFound();
        await chat.MarkReadAsync(me.CompanyId, me.DriverId, readerIsDriver: true, ct);
        return Ok();
    }

    // ---- Dispatch ----

    [HttpGet("threads")]
    public async Task<ActionResult<object>> Threads(CancellationToken ct)
    {
        if (!IsDispatch() || current.CompanyId is not Guid c) return Forbid();
        return Ok(await chat.ThreadsAsync(c, ct));
    }

    [HttpGet("drivers/{driverId:guid}")]
    public async Task<ActionResult<object>> Conversation(Guid driverId, CancellationToken ct)
    {
        if (!IsDispatch() || current.CompanyId is not Guid c) return Forbid();
        if (!await db.Drivers.AnyAsync(x => x.CompanyId == c && x.Id == driverId, ct)) return NotFound();
        return Ok(await chat.ConversationAsync(c, driverId, 200, ct));
    }

    [HttpPost("drivers/{driverId:guid}")]
    public async Task<ActionResult<ChatMessageDto>> SendToDriver(Guid driverId, [FromBody] ChatSendRequest request, CancellationToken ct)
    {
        if (!IsDispatch() || current.CompanyId is not Guid c) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest(new { code = "ValidationError", message = "Poruka je prazna." });
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == driverId, ct);
        if (driver is null) return NotFound();
        return Ok(await chat.SendAsync(c, driverId, fromDriver: false, request.Text, driver.CurrentTripId, ct));
    }

    [HttpPost("drivers/{driverId:guid}/read")]
    public async Task<IActionResult> ReadDriver(Guid driverId, CancellationToken ct)
    {
        if (!IsDispatch() || current.CompanyId is not Guid c) return Forbid();
        await chat.MarkReadAsync(c, driverId, readerIsDriver: false, ct);
        return Ok();
    }

    private bool IsDispatch()
        => current.IsAdministrator || current.IsInRole("Dispatcher") || current.IsInRole("Moderator") || current.HasPermission("dispatch.manage");

    private sealed record OwnDriver(Guid CompanyId, Guid DriverId, Guid? TripId);

    private async Task<OwnDriver?> OwnDriverAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c || current.UserId is not Guid userId) return null;
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == userId, ct);
        return driver is null ? null : new OwnDriver(c, driver.Id, driver.CurrentTripId);
    }
}

public sealed record ChatSendRequest(string? Text);
