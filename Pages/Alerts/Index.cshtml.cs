using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Alerts;

/// <summary>First load of /alerts, embedded in the page.</summary>
public sealed class IndexModel(AlertQueries alerts) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/alerts/feed", await alerts.FeedAsync(ct));
    }
}
