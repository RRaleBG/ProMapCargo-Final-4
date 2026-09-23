using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;


namespace ProMapCargo.Api.Controllers;

[ApiController]
//[Authorize]
[Route("api/operations")]
public sealed class OperationsController(ProMapCargoDbContext db,ICurrentUserContext current):ControllerBase
{
    Guid C()=>current.CompanyId??throw new UnauthorizedAccessException();
    [HttpGet("waiting")]public Task<List<WaitingEvent>> Waiting(CancellationToken ct)=>db.WaitingEvents.Where(x=>x.CompanyId==C()).OrderByDescending(x=>x.StartedAt).Take(500).ToListAsync(ct);
    [HttpGet("borders")]public Task<List<BorderCrossing>> Borders(CancellationToken ct)=>db.BorderCrossings.Where(x=>x.CompanyId==C()).OrderByDescending(x=>x.EnteredAt).Take(500).ToListAsync(ct);
    [HttpGet("costs")]public Task<List<TransportCost>> Costs(CancellationToken ct)=>db.TransportCosts.Where(x=>x.CompanyId==C()).OrderByDescending(x=>x.CreatedAt).Take(500).ToListAsync(ct);
    [HttpPost("waiting")]public async Task<ActionResult<WaitingEvent>> AddWaiting(WaitingEvent item,CancellationToken ct) {
        item.Id=Guid.NewGuid();
        item.CompanyId=C();
        if(item.StartedAt==default)item.StartedAt=DateTimeOffset.UtcNow;
        db.WaitingEvents.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }
    [HttpPost("costs")]public async Task<ActionResult<TransportCost>> AddCost(TransportCost item,CancellationToken ct) {
        item.Id=Guid.NewGuid();
        item.CompanyId=C();
        item.Amount=item.Quantity*item.UnitPrice;
        db.TransportCosts.Add(item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }
}
