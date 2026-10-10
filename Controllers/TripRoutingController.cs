using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Routing;
using ProMapCargo.Api.Services;
using System.Text.Json;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/trips/{tripId}/route")]
public sealed class TripRoutingController(ProMapCargoDbContext db, ICurrentUserContext current, IPostGisRoutingService routing) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RouteResponse>> Calculate(Guid tripId, [FromBody] RouteRequest request, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c)
        {
            return Unauthorized();
        }

        var trip = await db.Trips.SingleOrDefaultAsync(x => 
                                                        x.CompanyId == c && x.Id == tripId, ct);

        if (trip is null) 
            return NotFound(); // Return proper NotFound instead of Ok()

        var result = await routing.CalculateAsync(request, ct);
        if (result.Code != "Ok") 
            return Ok(result);

        var route = result.Routes[result.SelectedRouteIndex];
        await
            db.TripRoutes.Where(x => x.CompanyId == c && x.TripId == tripId && x.Status == TripRouteStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, TripRouteStatus.Superseded), ct);

        // SqlQueryRaw<long?> needs a column named "Value" and would throw here; the routing
        // service already reports the graph version it used.
        var version = result.Diagnostics.GraphVersion ?? 0;
        var tr = new TripRoute
        {
            Id = Guid.NewGuid(),
            CompanyId = c,
            TripId = tripId,
            GraphVersion = version,
            DistanceMeters = route.Distance,
            DurationSeconds = route.Duration,
            EstimatedArrival = (request.DepartureAt ?? DateTimeOffset.UtcNow).AddSeconds(route.Duration),
            GeometryJson = JsonSerializer.Serialize(route.Geometry),
            ManeuversJson = JsonSerializer.Serialize(result.Maneuvers),
            EdgeIdsJson = "[]",
            ActivatedAt = DateTimeOffset.UtcNow
        }
        ;
        db.TripRoutes.Add(tr);

        trip.ActiveRouteId = tr.Id;
        trip.PlannedDistanceMeters = route.Distance;
        trip.PlannedDurationSeconds = route.Duration;

        await db.SaveChangesAsync(ct);

        return Ok(result);
    }
}
