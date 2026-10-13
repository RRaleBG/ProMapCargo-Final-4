using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Trips;

/// <summary>First load of /trips, embedded in the page.</summary>
public sealed class IndexModel(BusinessService business) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/business/trips", await OrNull(() => business.TripsAsync(ct)));
        Seed("/api/business/drivers", await OrNull(() => business.DriversAsync(ct)));
        Seed("/api/business/vehicles", await OrNull(() => business.VehiclesAsync(ct)));
        Seed("/api/business/orders", await OrNull(() => business.OrdersAsync(ct)));
    }
}
