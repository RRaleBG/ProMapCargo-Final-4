using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Finance;

/// <summary>First load of /finance, in its default driver mode, embedded in the page.</summary>
public sealed class IndexModel(FinanceQueries finance) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/finance/overview?mode=driver", await finance.OverviewAsync(null, "driver", ct));
    }
}
