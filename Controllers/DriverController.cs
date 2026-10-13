using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;
namespace ProMapCargo.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator,Driver")]
[Route("api/driver")]
public sealed class DriverController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit) : ControllerBase
{
    // ---- /driver portal: active trip, stops and the driver's actions ----

    [HttpGet("portal")]
    public async Task<ActionResult<object>> Portal(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null)
            return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });

        var trip = await ActiveTripAsync(c, driver.Id, ct);
        var stops = trip is null ? [] : await db.TransportStops.AsNoTracking()
            .Where(x => x.CompanyId == c && x.TransportOrderId == trip.TransportOrderId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(ct);
        var next = stops.FirstOrDefault(x => x.Status is TransportStopStatus.Planned or TransportStopStatus.Arrived or TransportStopStatus.InService);
        var vehicle = trip?.VehicleId is { } vehicleId ? await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == vehicleId, ct) : null;
        var phone = await db.Companies.AsNoTracking().Where(x => x.Id == c).Select(x => x.DispatchPhone).FirstOrDefaultAsync(ct);

        return Ok(new
        {
            Driver = new { driver.Id, driver.FullName, driver.EmployeeNumber, Status = driver.Status.ToString() },
            Trip = trip is null ? null : new
            {
                trip.Id,
                trip.TripNumber,
                Status = trip.Status.ToString(),
                State = trip.ExecutionState.ToString(),
                trip.RouteProgressPercent,
                trip.PlannedArrivalAt,
                Route = stops.Count == 0 ? "" : (stops[0].City ?? stops[0].Name) + " → " + (stops[^1].City ?? stops[^1].Name),
                Vehicle = vehicle?.Registration,
            },
            Stops = stops.Select(x => new
            {
                x.Id, x.Sequence, Type = x.Type.ToString(), Status = x.Status.ToString(), x.Name, x.City, x.CountryCode,
                x.PlannedAt, x.ArrivedAt, x.CompletedAt, x.Notes,
            }),
            NextStopId = next?.Id,
            DispatchPhone = phone,
        });
    }

    [HttpPost("stops/{stopId:guid}/arrive")]
    public Task<IActionResult> Arrive(Guid stopId, CancellationToken ct) => UpdateStop(stopId, arrive: true, ct);

    [HttpPost("stops/{stopId:guid}/complete")]
    public Task<IActionResult> Complete(Guid stopId, CancellationToken ct) => UpdateStop(stopId, arrive: false, ct);

    [HttpPost("pause")]
    public async Task<IActionResult> Pause(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null) return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });
        var trip = await ActiveTripAsync(c, driver.Id, ct, tracked: true);
        if (trip is null || trip.Status is not (TripStatus.Active or TripStatus.Paused))
            return Conflict(new { code = "Conflict", message = "Nema ture u toku." });

        // The same button starts and ends the break.
        var pausing = trip.Status == TripStatus.Active;
        trip.Status = pausing ? TripStatus.Paused : TripStatus.Active;
        trip.ExecutionState = pausing ? TripExecutionState.Waiting : TripExecutionState.InTransit;
        audit.Add(pausing ? "Driver.PauseStart" : "Driver.PauseEnd", nameof(Trip), trip.Id, new { trip.TripNumber });
        await db.SaveChangesAsync(ct);
        return Ok(new { Status = trip.Status.ToString() });
    }

    [HttpPost("incident")]
    public async Task<IActionResult> Incident([FromBody] IncidentRequest request, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { code = "ValidationError", message = "Opišite incident." });
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null) return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });
        var trip = await ActiveTripAsync(c, driver.Id, ct);

        var alert = new OperationalAlert
        {
            Id = Guid.NewGuid(),
            CompanyId = c,
            Type = "Incident",
            Severity = request.Severity is "Critical" or "Warning" ? request.Severity : "Critical",
            Status = "Open",
            TripId = trip?.Id,
            VehicleId = trip?.VehicleId ?? driver.CurrentVehicleId,
            DriverId = driver.Id,
            Message = request.Message.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            Source = "Driver",
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { reportedBy = driver.FullName }),
        };
        db.OperationalAlerts.Add(alert);
        audit.Add("Driver.Incident", nameof(OperationalAlert), alert.Id, new { alert.Message, alert.Severity });
        await db.SaveChangesAsync(ct);
        return Ok(new { alert.Id });
    }

    private async Task<IActionResult> UpdateStop(Guid stopId, bool arrive, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null) return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });
        var trip = await ActiveTripAsync(c, driver.Id, ct, tracked: true);
        var stop = trip is null ? null : await db.TransportStops.SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == stopId && x.TransportOrderId == trip.TransportOrderId, ct);
        if (trip is null || stop is null) return NotFound(new { code = "NotFound", message = "Stanica nije deo aktivne ture." });

        var now = DateTimeOffset.UtcNow;
        if (arrive)
        {
            if (stop.Status != TransportStopStatus.Planned) return Conflict(new { code = "Conflict", message = "Dolazak na ovu stanicu je već evidentiran." });
            stop.Status = TransportStopStatus.Arrived;
            stop.ArrivedAt = now;
            trip.ExecutionState = stop.Type switch
            {
                TransportStopType.Loading => TripExecutionState.Loading,
                TransportStopType.Unloading => TripExecutionState.Unloading,
                TransportStopType.Border => TripExecutionState.BorderCrossing,
                _ => TripExecutionState.AtStop,
            };
        }
        else
        {
            if (stop.Status is not (TransportStopStatus.Arrived or TransportStopStatus.InService)) return Conflict(new { code = "Conflict", message = "Prvo evidentirajte dolazak na stanicu." });
            stop.Status = TransportStopStatus.Completed;
            stop.CompletedAt = now;
            stop.WaitingMinutes = stop.ArrivedAt is { } arrived ? Math.Max(0, (int)(now - arrived).TotalMinutes - stop.ServiceMinutes) : 0;

            var remaining = await db.TransportStops.CountAsync(x => x.CompanyId == c && x.TransportOrderId == trip.TransportOrderId && x.Id != stop.Id
                                                                    && x.Status != TransportStopStatus.Completed && x.Status != TransportStopStatus.Skipped, ct);
            if (remaining == 0)
            {
                trip.Status = TripStatus.Completed;
                trip.ExecutionState = TripExecutionState.Completed;
                trip.CompletedAt = now;
                trip.RouteProgressPercent = 100;
            }
            else
            {
                trip.Status = TripStatus.Active;
                trip.ExecutionState = TripExecutionState.InTransit;
                trip.StartedAt ??= now;
            }
        }

        audit.Add(arrive ? "Driver.StopArrive" : "Driver.StopComplete", nameof(TransportStop), stop.Id, new { stop.Name, stop.City, trip.TripNumber });
        await db.SaveChangesAsync(ct);
        return Ok(new { Status = stop.Status.ToString(), TripStatus = trip.Status.ToString() });
    }

    private Task<Trip?> ActiveTripAsync(Guid c, Guid driverId, CancellationToken ct, bool tracked = false)
    {
        var trips = tracked ? db.Trips : db.Trips.AsNoTracking();
        return trips.Where(x => x.CompanyId == c && x.DriverId == driverId && (x.Status == TripStatus.Active || x.Status == TripStatus.Paused || x.Status == TripStatus.Planned))
            .OrderByDescending(x => x.Status == TripStatus.Active || x.Status == TripStatus.Paused)
            .ThenBy(x => x.PlannedStartAt)
            .FirstOrDefaultAsync(ct);
    }

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
    public async Task<IActionResult> Accept(Guid dispatchId, CancellationToken ct) => await SetDispatch(dispatchId, RouteDispatchStatus.Accepted, ct) ? Ok(new { status = "accepted" }) : NotFound();

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
        return Ok(new { status = "rejected", reason = req.Reason });
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

public sealed record IncidentRequest(string? Message, string? Severity);
