using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Route("api/geocode")]
public sealed class GeocodingController(
    IGeocodingService service
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return BadRequest(
                new
                {
                    code = "InvalidQuery",
                    message = "Parametar q mora imati najmanje 2 karaktera."
                }
            );
        }

        var result = await service.SearchAsync(q.Trim(), Math.Clamp(limit, 1, 10), ct);

        return Ok(result);
    }
}