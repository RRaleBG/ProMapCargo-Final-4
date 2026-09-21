using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;
namespace ProMapCargo.Api.Controllers;

[ApiController]
[Authorize(Roles = "Driver")]
[Route("api/driver")]
public sealed class DriverController(ProMapCargoDbContext db, ICurrentUserContext current) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<object>> Dashboard(CancellationToken ct)
    {
        var c = current.CompanyId ?? throw new UnauthorizedAccessException();
        var driver = await db.Drivers.SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);

        if (driver is null)
            return NotFound();

        var trip = driver.CurrentTripId.HasValue ? await db.Trips.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == driver.CurrentTripId, ct) : null;

        var pending = await
            db.RouteDispatches
            .Where(x => x.CompanyId == c && x.DriverId == driver.Id && (x.Status == RouteDispatchStatus.Pending || x.Status == RouteDispatchStatus.Sent))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return Ok(new
        {
            driver,
            activeTrip = trip,
            pendingRoute = pending
        }
        );
    }
    [HttpPost("routes/{dispatchId}/accept")] 
    public async Task<IActionResult> Accept(Guid dispatchId, CancellationToken ct) => await SetDispatch(dispatchId, RouteDispatchStatus.Accepted, ct) ? Ok() : NotFound();
 
    [HttpPost("routes/{dispatchId}/reject")]
    public async Task<IActionResult> Reject(Guid dispatchId, [FromBody] RejectRequest req, CancellationToken ct)
    {
        var c = current.CompanyId ?? throw new UnauthorizedAccessException();
        var d = await db.RouteDispatches.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == dispatchId && x.DriverId == DriverId(), ct);
        if (d is null)
            return NotFound();

        d.Status = RouteDispatchStatus.Rejected;
        d.RejectedAt = DateTimeOffset.UtcNow;
        d.RejectionReason = req.Reason;

        await db.SaveChangesAsync(ct);
        return Ok();
    }
    [HttpPost("trips/{tripId}/start")]
    public async Task<IActionResult> Start(Guid tripId, CancellationToken ct)
    {
        var c = current.CompanyId ?? throw new UnauthorizedAccessException();
        var d = await db.Drivers.SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (d is null) return NotFound();
        var t = await db.Trips.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == tripId && x.DriverId == d.Id, ct);
        if (t is null) return NotFound();
        t.Status = TripStatus.Active;
        t.ExecutionState = TripExecutionState.InTransit;
        t.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }
    Guid DriverId() => db.Drivers.AsNoTracking().Where(x => x.CompanyId == current.CompanyId && x.UserId == current.UserId).Select(x => x.Id).First();
    async Task<bool> SetDispatch(Guid id, RouteDispatchStatus status, CancellationToken ct)
    {
        var c = current.CompanyId ?? throw new UnauthorizedAccessException();
        var d = await db.RouteDispatches.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == id && x.DriverId == DriverId(), ct);
        if (d is null) return false;
        d.Status = status;
        if (status == RouteDispatchStatus.Accepted) d.AcceptedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
public sealed record RejectRequest(string? Reason);
