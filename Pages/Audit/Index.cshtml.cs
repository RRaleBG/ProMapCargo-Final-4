using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Audit;

/// <summary>First load of /audit, without filters, embedded in the page.</summary>
public sealed class IndexModel(AuditQueries audit) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/audit", await audit.ListAsync(null, null, null, null, ct));
    }
}
