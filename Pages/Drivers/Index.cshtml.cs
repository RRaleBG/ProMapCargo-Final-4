using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Drivers;

/// <summary>First load of /drivers, embedded in the page.</summary>
public sealed class IndexModel(BusinessService business) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/business/drivers", await OrNull(() => business.DriversAsync(ct)));
        Seed("/api/business/vehicles", await OrNull(() => business.VehiclesAsync(ct)));
        Seed("/api/business/trips", await OrNull(() => business.TripsAsync(ct)));
    }
}
