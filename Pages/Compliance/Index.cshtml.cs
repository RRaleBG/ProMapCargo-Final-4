using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Compliance;

/// <summary>First load of /compliance, embedded in the page.</summary>
public sealed class IndexModel(ComplianceQueries compliance) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/compliance", await compliance.OverviewAsync(ct));
    }
}
