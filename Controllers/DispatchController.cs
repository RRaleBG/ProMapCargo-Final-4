using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>/dispatch (Command Center): fleet on the map, active trips, events, fleet status, borders.</summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/dispatch")]
public sealed class DispatchController(DispatchQueries queries) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<object>> Overview(CancellationToken ct)
    {
        var data = await queries.OverviewAsync(ct);
        return data is null ? Unauthorized() : Ok(data);
    }
}
