using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>Download of an uploaded receipt or document, only within the user's company.</summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/files")]
public sealed class FilesController(ProMapCargoDbContext db, ICurrentUserContext current, FileStorageService files) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return Unauthorized();
        var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.Id == id, ct);
        if (file is null) return NotFound();

        var path = files.FullPath(file);
        return System.IO.File.Exists(path) ? PhysicalFile(path, file.ContentType, file.FileName) : NotFound();
    }
}
