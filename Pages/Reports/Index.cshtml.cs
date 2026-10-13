using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Reports;

/// <summary>First load of /reports: key figures (the reports themselves are generated on request), embedded in the page.</summary>
public sealed class IndexModel(ReportQueries reports) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/reports/kpi", await reports.KpiAsync(ct));
    }
}
