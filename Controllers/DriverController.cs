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
public sealed class DriverController(ProMapCargoDbContext db, ICurrentUserContext current, AuditWriter audit, DriverPortalQueries portalQueries) : ControllerBase
{
    // ---- /driver portal: active trip, stops and the driver's actions ----

    [HttpGet("portal")]
    public async Task<ActionResult<object>> Portal(CancellationToken ct)
    {
        var portal = await portalQueries.PortalAsync(ct);
        if (portal is null) return Unauthorized();
        return portal.Data is null
            ? NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." })
            : Ok(portal.Data);
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
        => portalQueries.ActiveTripAsync(c, driverId, ct, tracked);

    // ---- Mobile app: messages and profile of the signed-in driver ----

    /// <summary>
    /// What concerns this driver, newest first (last 30 days): alerts on the driver, the
    /// driver's trips or vehicle, routes dispatch sent, incidents the driver reported.
    /// Unlike /api/alerts it never shows other drivers' alerts.
    /// </summary>
    [HttpGet("messages")]
    public async Task<ActionResult<object>> Messages(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null) return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });

        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var tripIds = await db.Trips.AsNoTracking()
            .Where(x => x.CompanyId == c && x.DriverId == driver.Id)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var alerts = await db.OperationalAlerts.AsNoTracking()
            .Where(x => x.CompanyId == c && x.CreatedAt >= since &&
                        (x.DriverId == driver.Id || (x.TripId != null && tripIds.Contains(x.TripId.Value)) ||
                         (driver.CurrentVehicleId != null && x.VehicleId == driver.CurrentVehicleId)))
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => new
            {
                x.Id,
                Kind = x.Source == "Driver" ? "incident" : "alert",
                x.Type,
                x.Severity,
                x.Status,
                x.Message,
                x.CreatedAt,
                x.ResolvedAt,
                x.TripId,
            })
            .ToListAsync(ct);

        var tripNumbers = await db.Trips.AsNoTracking()
            .Where(x => x.CompanyId == c && tripIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.TripNumber, ct);

        var routes = await db.RouteDispatches.AsNoTracking()
            .Where(x => x.CompanyId == c && x.DriverId == driver.Id && x.CreatedAt >= since)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new { x.Id, x.TripId, Status = x.Status.ToString(), x.CreatedAt, x.SentAt, x.AcceptedAt, x.RejectedAt, x.RejectionReason })
            .ToListAsync(ct);

        var messages = alerts.Select(x => new DriverMessage(
                x.Id, x.Kind, x.Type, x.Severity, x.Status, x.Message, x.CreatedAt,
                x.TripId, x.TripId is { } t && tripNumbers.TryGetValue(t, out var n) ? n : null))
            .Concat(routes.Select(x => new DriverMessage(
                x.Id, "route", "Route", x.Status == "Rejected" ? "Warning" : "Info", x.Status,
                x.Status switch
                {
                    "Accepted" => "Ruta je prihvaćena.",
                    "Rejected" => string.IsNullOrWhiteSpace(x.RejectionReason) ? "Ruta je odbijena." : $"Ruta je odbijena: {x.RejectionReason}",
                    "Started" => "Vožnja po ruti je počela.",
                    "Superseded" => "Dispečer je poslao noviju rutu.",
                    "Cancelled" => "Dispečer je otkazao rutu.",
                    "Expired" => "Ruta je istekla bez odgovora.",
                    _ => "Dispečer je poslao novu rutu. Odgovorite na početnom ekranu.",
                },
                x.SentAt ?? x.CreatedAt, x.TripId, tripNumbers.TryGetValue(x.TripId, out var n) ? n : null)))
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        return Ok(messages);
    }

    /// <summary>The driver's own record for the app: documents with expiry, vehicle, trips done.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<object>> Profile(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null) return NotFound(new { code = "NoDriver", message = "Nalog nije povezan sa vozačem." });

        var vehicle = driver.CurrentVehicleId is { } vehicleId
            ? await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == vehicleId, ct)
            : null;
        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var completed = await db.Trips.AsNoTracking().CountAsync(x => x.CompanyId == c && x.DriverId == driver.Id && x.Status == TripStatus.Completed, ct);
        var completedThisMonth = await db.Trips.AsNoTracking().CountAsync(x => x.CompanyId == c && x.DriverId == driver.Id && x.Status == TripStatus.Completed && x.CompletedAt >= monthStart, ct);

        return Ok(new
        {
            driver.Id,
            driver.FullName,
            driver.EmployeeNumber,
            Status = driver.Status.ToString(),
            driver.Phone,
            driver.Email,
            driver.LicenseNumber,
            driver.LicenseCategories,
            driver.LicenseExpiry,
            driver.Code95Expiry,
            driver.AdrExpiry,
            driver.TachographCardNumber,
            driver.TachographCardExpiry,
            driver.MedicalExpiry,
            driver.HireDate,
            Vehicle = vehicle is null ? null : new
            {
                vehicle.Id,
                vehicle.Registration,
                vehicle.Make,
                vehicle.Model,
                vehicle.GrossWeightTons,
                vehicle.HeightMeters,
                vehicle.WidthMeters,
                vehicle.LengthMeters,
                vehicle.Axles,
            },
            CompletedTrips = completed,
            CompletedTripsThisMonth = completedThisMonth,
        });
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

/// <summary>One entry of the driver's message list (mobile "Poruke").</summary>
public sealed record DriverMessage(
    Guid Id,
    string Kind,
    string Type,
    string Severity,
    string Status,
    string Text,
    DateTimeOffset CreatedAt,
    Guid? TripId,
    string? TripNumber);
