using Microsoft.AspNetCore.Mvc;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Route("styles")]
public sealed class LocalMapTilesController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public LocalMapTilesController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet("serbia.pmtiles")]
    public IActionResult Serbia()
    {
        var filePath = Path.Combine( _environment.WebRootPath, "maps", "serbia.pmtiles");

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        return PhysicalFile(filePath,"application/octet-stream", enableRangeProcessing: true);
    }
}