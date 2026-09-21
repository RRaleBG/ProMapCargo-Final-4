using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Routing;
using ProMapCargo.Api.Services;
namespace ProMapCargo.Api.Controllers;
[ApiController][Authorize][Route("api/trips/{tripId}/route")]
public sealed class TripRoutingController(ProMapCargoDbContext db,ICurrentUserContext current,IPostGisRoutingService routing):ControllerBase
{
    [HttpPost]public async Task<ActionResult<RouteResponse>> Calculate(Guid tripId,[FromBody]RouteRequest request,CancellationToken ct) {
        var c=current.CompanyId??throw new UnauthorizedAccessException();
        var trip=await db.Trips.SingleOrDefaultAsync(x=>x.CompanyId==c&&x.Id==tripId,ct);
        if(trip is null)return NotFound();
        var result=await routing.CalculateAsync(request,ct);
        if(result.Code!="Ok")return Ok(result);
        var route=result.Routes[result.SelectedRouteIndex];
        await
            db.TripRoutes.Where(x=>x.CompanyId==c&&x.TripId==tripId&&x.Status==TripRouteStatus.Active).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,TripRouteStatus.Superseded),ct);
        var version=await db.Database.SqlQueryRaw<long?>("SELECT graph_version FROM routing_graph_versions WHERE status='ready' AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1").SingleOrDefaultAsync(ct)??0;
        var tr=new TripRoute {
            Id=Guid.NewGuid(),CompanyId=c,TripId=tripId,GraphVersion=version,DistanceMeters=route.Distance,DurationSeconds=route.Duration,EstimatedArrival=(request.DepartureAt??DateTimeOffset.UtcNow).AddSeconds(route.Duration),GeometryJson=JsonSerializer.Serialize(route.Geometry),ManeuversJson="[]",EdgeIdsJson="[]",ActivatedAt=DateTimeOffset.UtcNow
        }
        ;
        db.TripRoutes.Add(tr);
        trip.ActiveRouteId=tr.Id;
        trip.PlannedDistanceMeters=route.Distance;
        trip.PlannedDurationSeconds=route.Duration;
        await db.SaveChangesAsync(ct);
        return Ok(result);
    }
}
