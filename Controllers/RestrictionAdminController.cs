using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Controllers;
[ApiController]
[Authorize(Roles = "Administrator,Moderator")]
[Route("api/admin/restrictions")]
public sealed class RestrictionAdminController(ProMapCargoDbContext db) : ControllerBase
{
    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken ct)
    => Ok(new {
        count = await db.RoadRestrictions.LongCountAsync(ct)
    }
    );
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] RoadRestriction restriction, CancellationToken ct)
    {
        if (restriction.Geometry is null)
        return BadRequest(new {
            message = "Geometry je obavezna."
        }
        );
        restriction.Geometry.SRID = 4326;
        db.RoadRestrictions.Add(restriction);
        await db.SaveChangesAsync(ct);
        return Created($"/api/admin/restrictions/{restriction.Id}", restriction);
    }
}
