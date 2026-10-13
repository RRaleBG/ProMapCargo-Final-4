using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>
/// /moderation: change requests waiting for a recorded decision. Approving or rejecting
/// only records the decision (who, when, why); nothing is applied to routes or roads.
/// </summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/moderation")]
public sealed class ModerationController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit, ModerationQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> List(CancellationToken ct)
    {
        var data = await queries.ListAsync(ct);
        return data is null ? Unauthorized() : Ok(data);
    }

    [HttpPost("{id:guid}/decision")]
    [Authorize(Roles = "Administrator,Moderator,Dispatcher")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] ModerationDecision request, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        if (request.Status is not ("approved" or "rejected" or "changes"))
            return BadRequest(new { code = "ValidationError", message = "Odluka mora biti odobreno, odbijeno ili traži izmene." });
        if (request.Status is "rejected" or "changes" && string.IsNullOrWhiteSpace(request.Note))
            return BadRequest(new { code = "ValidationError", message = "Unesite obrazloženje odluke." });

        var item = await db.ModerationRequests.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status != "pending")
            return Conflict(new { code = "Conflict", message = "O zahtevu je već odlučeno." });

        item.Status = request.Status;
        item.DecidedAt = DateTimeOffset.UtcNow;
        item.DecidedByUserId = current.UserId;
        item.DecidedByName = User.FindFirst("display_name")?.Value ?? User.Identity?.Name;
        item.DecisionNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        audit.Add(request.Status switch { "approved" => "Moderation.Approve", "rejected" => "Moderation.Reject", _ => "Moderation.RequestChanges" }, "ModerationRequest", item.Id, new { item.Number, item.Status, item.DecisionNote });
        await db.SaveChangesAsync(ct);
        return Ok(new { item.Id, item.Status });
    }
}

public sealed record ModerationDecision(string Status, string? Note);
