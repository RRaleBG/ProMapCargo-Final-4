using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;
namespace ProMapCargo.Api.Controllers;
[ApiController][Authorize][Route("api/business")]
public sealed class BusinessController(BusinessService service):ControllerBase {
    [HttpGet("dashboard")]public Task<object> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
    [HttpGet("vehicles")]public Task<List<Vehicle>> Vehicles(CancellationToken ct)=>service.VehiclesAsync(ct);
    [HttpGet("drivers")]public Task<List<Driver>> Drivers(CancellationToken ct)=>service.DriversAsync(ct);
    [HttpGet("orders")]public Task<List<TransportOrder>> Orders(CancellationToken ct)=>service.OrdersAsync(ct);
    [HttpGet("trips")]public Task<List<Trip>> Trips(CancellationToken ct)=>service.TripsAsync(ct);
    [HttpGet("trips/{tripId}/route")]public async Task<ActionResult<TripRoute>> ActiveRoute(Guid tripId,CancellationToken ct)=>Ok(await service.ActiveRouteAsync(tripId,ct));
    [HttpPost("trips/{tripId}/route/dispatch")][Authorize(Roles="Administrator,Dispatcher")]
    public async Task<ActionResult<RouteDispatch>> Dispatch(Guid tripId,[FromBody]DispatchRequest request,CancellationToken
        ct)=>Ok(await service.DispatchAsync(tripId,request.RouteId,ct));
}
public sealed record DispatchRequest(Guid RouteId);
