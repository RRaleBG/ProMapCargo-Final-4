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
            return Ok(new { }); // Return empty object instead of no content
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
            return Ok(new List<Vehicle>()); // Return empty list instead of no content
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
            return Ok(new List<Driver>()); // Return empty list instead of no content
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
            return Ok(new List<TransportOrder>()); // Return empty list instead of no content
        }
    }



    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult<TransportOrder>> Order(Guid orderId, CancellationToken ct)
    {
        try
        {
            var order = await service.OrderAsync(orderId, ct);

            return order is null ? NotFound() : Ok(order);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }


    [HttpPost("orders")]
    public async Task<ActionResult<TransportOrder>> CreateOrder( [FromBody] CreateTransportOrderRequest request,  CancellationToken ct)
    {
        try
        {
            var order =  await service.CreateOrderAsync(request, ct);

            return CreatedAtAction(
                nameof(Order),
                new
                {
                    orderId = order.Id
                },
                order);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(
                new
                {
                    code = "ValidationError",
                    message = ex.Message
                });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    code = "OrderConflict",
                    message = ex.Message
                });
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
            return Ok(new List<Trip>()); // Return empty list instead of no content
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
            return Ok((TripRoute?)null); // Return null object instead of no content
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
            return Ok((RouteDispatch?)null); // Return null object instead of no content
        }
    }
}

public sealed record DispatchRequest(Guid RouteId);
