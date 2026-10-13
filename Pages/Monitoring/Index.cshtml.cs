using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Monitoring;

/// <summary>First load of /monitoring: fleet positions from the dispatch overview, embedded in the page.</summary>
public sealed class IndexModel(DispatchQueries dispatch) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/dispatch/overview", await dispatch.OverviewAsync(ct));
    }
}
