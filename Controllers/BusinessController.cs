using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

[ApiController]
//[Authorize(Policy = "AppOrMobile")]
[Route("api/business")]
public sealed class BusinessController(BusinessService service) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<object>> Dashboard(CancellationToken ct)
    {
        try
        {
            return Ok(await service.DashboardAsync(ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("vehicles")]
    public async Task<ActionResult<List<Vehicle>>> Vehicles(CancellationToken ct)
    {
        try
        {
            return Ok(await service.VehiclesAsync(ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("drivers")]
    public async Task<ActionResult<List<Driver>>> Drivers(CancellationToken ct)
    {
        try
        {
            return Ok(await service.DriversAsync(ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("orders")]
    public async Task<ActionResult<List<TransportOrder>>> Orders(CancellationToken ct)
    {
        try
        {
            return Ok(await service.OrdersAsync(ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("trips")]
    public async Task<ActionResult<List<Trip>>> Trips(CancellationToken ct)
    {
        try
        {
            return Ok(await service.TripsAsync(ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("trips/{tripId}/route")]
    public async Task<ActionResult<TripRoute>> ActiveRoute(Guid tripId, CancellationToken ct)
    {
        try
        {
            return Ok(await service.ActiveRouteAsync(tripId, ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("trips/{tripId}/route/dispatch")]
    //[Authorize(Roles="Administrator,Dispatcher")]
    public async Task<ActionResult<RouteDispatch>> Dispatch(Guid tripId, [FromBody] DispatchRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await service.DispatchAsync(tripId, request.RouteId, ct));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }
}

public sealed record DispatchRequest(Guid RouteId);
