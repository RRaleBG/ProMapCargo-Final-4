using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>/audit: the change log with filters and CSV export.</summary>
[ApiController]
[Authorize(Policy = "AppOrMobile")]
[Route("api/audit")]
public sealed class AuditController(AuditQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? user, [FromQuery] string? action, [FromQuery] string? entity,
        [FromQuery] DateOnly? from, [FromQuery] string? format, CancellationToken ct)
    {
        var rows = await queries.ListAsync(user, action, entity, from, ct);
        if (rows is null) return Unauthorized();

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = new StringBuilder("Vreme;Korisnik;Akcija;Entitet;ID;Nova vrednost;IP;Izvor\n");
            foreach (var r in rows)
            {
                csv.AppendLine(string.Join(';', new[] { r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), r.User, r.Action, r.EntityType, r.EntityId, r.NewValueJson ?? "", r.IpAddress ?? "", r.Source }
                    .Select(v => "\"" + v.Replace("\"", "\"\"") + "\"")));
            }

            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", $"audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }

        return Ok(rows);
    }
}
