using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;


[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit, AlertQueries queries) : ControllerBase
{
    // Alert center (/alerts): alerts with the vehicle, driver and trip they concern.
    [HttpGet("feed")]
    public async Task<IActionResult> Feed(CancellationToken ct)
    {
        var data = await queries.FeedAsync(ct);
        return data is null ? Unauthorized() : Ok(data);
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] AlertStatusRequest request, CancellationToken ct)
    {
        if (current.CompanyId is not Guid companyId)
            return Unauthorized();
        if (request.Status is not ("Open" or "Acknowledged" or "Snoozed" or "Resolved"))
            return BadRequest(new { code = "ValidationError", message = "Status upozorenja nije ispravan." });

        var alert = await db.OperationalAlerts.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
        if (alert is null)
            return NotFound();

        var old = alert.Status;
        alert.Status = request.Status;
        alert.ResolvedAt = request.Status == "Resolved" ? DateTimeOffset.UtcNow : null;
        audit.Add("Alert." + request.Status, "OperationalAlert", alert.Id, new { alert.Type, Status = alert.Status }, new { Status = old });
        await db.SaveChangesAsync(ct);
        return Ok(new { alert.Id, alert.Status, alert.ResolvedAt });
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
    {
        if (current.CompanyId is not Guid companyId)
            return Unauthorized();

        var alert = await db.OperationalAlerts.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
        if (alert is null)
            return NotFound();

        if (alert.Status != "Resolved")
        {
            alert.Status = "Resolved";
            alert.ResolvedAt = DateTimeOffset.UtcNow;
            audit.Add("Alert.Resolve", "OperationalAlert", alert.Id, new { alert.Type, alert.Message });
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { alert.Id, alert.Status, alert.ResolvedAt });
    }

    [HttpGet("count")]
    [HttpHead("count")]
    public async Task<ActionResult<object>> Count(CancellationToken ct)
    {
        if (current.CompanyId is not Guid companyId)
            return Ok(new { count = 0 }); // Return empty count instead of no content

        var count = await db.OperationalAlerts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == "Open")
            .CountAsync(ct);

        return Ok(new
        {
            count
        });
    }

    [HttpGet]
    public async Task<ActionResult<object>> List([FromQuery] bool summary = false, CancellationToken ct = default)
    {
        if (current.CompanyId is not Guid companyId)
            return Ok(new List<object>()); // Return empty list instead of no content

        var query = db.OperationalAlerts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (summary)
        {
            var alerts = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => new
                {
                    x.Id,
                    x.Type,
                    x.Severity,
                    x.Status,
                    x.TripId,
                    x.VehicleId,
                    x.DriverId,
                    x.Message,
                    x.CreatedAt,
                    x.ResolvedAt,
                    x.Source
                })
                .ToListAsync(ct);

            return Ok(alerts);
        }

        var result = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        return Ok(result);
    }
}
public sealed record AlertStatusRequest(string Status);
