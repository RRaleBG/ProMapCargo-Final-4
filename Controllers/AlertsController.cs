using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;


//[Authorize]
[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(ProMapCargoDbContext db, ICurrentUserContext current) : ControllerBase
{
    [HttpGet("count")]
    public async Task<ActionResult<object>> Count(CancellationToken ct)
    {
        if (current.CompanyId is not Guid companyId)
            return Ok(new { count = 0 }); // Return empty count instead of no content

        var count = await db.OperationalAlerts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == "Open")
            .CountAsync(ct);

        return Ok(new
        {
            count
        });
    }

    [HttpGet]
    public async Task<ActionResult<object>> List([FromQuery] bool summary = false, CancellationToken ct = default)
    {
        if (current.CompanyId is not Guid companyId)
            return Ok(new List<object>()); // Return empty list instead of no content

        var query = db.OperationalAlerts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (summary)
        {
            var alerts = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => new
                {
                    x.Id,
                    x.Type,
                    x.Severity,
                    x.Status,
                    x.TripId,
                    x.VehicleId,
                    x.DriverId,
                    x.Message,
                    x.CreatedAt,
                    x.ResolvedAt,
                    x.Source
                })
                .ToListAsync(ct);

            return Ok(alerts);
        }

        var result = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        return Ok(result);
    }
}