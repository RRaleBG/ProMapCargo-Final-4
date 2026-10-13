using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Orders;

/// <summary>First load of /orders, embedded in the page.</summary>
public sealed class IndexModel(BusinessService business) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/business/orders/overview", await OrNull(() => business.OrdersOverviewAsync(ct)));
    }
}
