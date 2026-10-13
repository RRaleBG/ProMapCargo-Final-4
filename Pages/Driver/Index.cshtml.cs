using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Driver;

/// <summary>First load of /driver portal, embedded in the page.</summary>
public sealed class IndexModel(DriverPortalQueries portal) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        // Same roles as DriverController; an account without a driver gets the API's 404 message.
        if (User.IsInRole("Administrator") || User.IsInRole("Driver"))
            Seed("/api/driver/portal", (await portal.PortalAsync(ct))?.Data);
    }
}
