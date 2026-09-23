using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;
namespace ProMapCargo.Api.Controllers;

[ApiController]
//[Authorize]
[Route("api/navigation/gps")]
public sealed class NavigationTelemetryController(ProMapCargoDbContext db,ICurrentUserContext current,IHubContext<NavigationHub>
    hub):ControllerBase
{
    [HttpPost]public async Task<IActionResult> Post([FromBody]GpsPositionRequest r,CancellationToken ct) {
        var c=current.CompanyId??throw new UnauthorizedAccessException();
        var v=await db.Vehicles.SingleOrDefaultAsync(x=>x.CompanyId==c&&x.Id==r.VehicleId,ct);
        if(v is null)return NotFound();
        var e=new VehiclePositionEvent {
            Id=Guid.NewGuid(),CompanyId=c,VehicleId=v.Id,TripId=r.TripId,Latitude=r.Latitude,Longitude=r.Longitude,AccuracyMeters=r.AccuracyMeters,SpeedKmh=r.SpeedKmh,Bearing=r.Bearing,Timestamp=r.Timestamp??DateTimeOffset.UtcNow,Source=PositionSource.Gps,Quality=r.AccuracyMeters
                is
                null?PositionQuality.Good:r.AccuracyMeters<=15?PositionQuality.Excellent:r.AccuracyMeters<=50?PositionQuality.Good:PositionQuality.Degraded
        }
        ;
        db.VehiclePositionEvents.Add(e);
        if(r.TripId.HasValue) {
            var t=await db.Trips.SingleOrDefaultAsync(x=>x.CompanyId==c&&x.Id==r.TripId,ct);
            if(t is not null) {
                t.CurrentLatitude=r.Latitude;
                t.CurrentLongitude=r.Longitude;
                t.CurrentSpeedKmh=r.SpeedKmh;
                t.CurrentBearing=r.Bearing;
                t.LastGpsAt=e.Timestamp;
                if(t.Status==TripStatus.Active)t.ExecutionState=TripExecutionState.InTransit;
            }
        }
        await db.SaveChangesAsync(ct);
        await hub.Clients.Group($"company:{c}").SendAsync("vehiclePositionUpdated",r,ct);
        if(r.TripId.HasValue)await hub.Clients.Group($"trip:{r.TripId}").SendAsync("gpsUpdated",r,ct);
        return Ok();
    }
}
public sealed record GpsPositionRequest(Guid VehicleId,Guid? TripId,double Latitude,double Longitude,double?
    AccuracyMeters,double? SpeedKmh,double? Bearing,DateTimeOffset? Timestamp);
