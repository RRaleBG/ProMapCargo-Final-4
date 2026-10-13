using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Dispatch;

/// <summary>First load of /dispatch (Command Center), embedded in the page.</summary>
public sealed class IndexModel(DispatchQueries dispatch) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/dispatch/overview", await dispatch.OverviewAsync(ct));
    }
}
