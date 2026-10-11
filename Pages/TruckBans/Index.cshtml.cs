using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.TruckBans;

/// <summary>
/// Review of the truck bans read from truckban.eu: new or changed bans and bans removed at the
/// source wait here, and the weight limit of each country is set from the source text.
/// </summary>
[Authorize(Policy = "compliance.manage")]
public sealed class IndexModel(TruckBanRepository repository, TruckBanSyncService sync) : PageModel
{
    public IReadOnlyList<TruckBan> ForReview { get; private set; } = [];
    public IReadOnlyList<TruckBan> Upcoming { get; private set; } = [];
    public IReadOnlyList<TruckBanCountry> Countries { get; private set; } = [];
    public TruckBanSyncService.SyncResult? LastSync => sync.LastResult;
    public DateTime? LastSyncUtc => sync.LastRunUtc;

    public async Task OnGetAsync(CancellationToken ct)
    {
        ForReview = await repository.GetForReviewAsync(ct);
        Upcoming = await repository.GetApprovedAsync(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(14), ct);
        Countries = await repository.GetCountriesAsync(ct);
    }

    public async Task<IActionResult> OnPostReviewAsync(long[] ids, bool approve, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            TempData["Toast.Error"] = "Nije izabrana nijedna zabrana.";
            return RedirectToPage();
        }

        var changed = await repository.ReviewAsync(ids, approve, User.Identity?.Name ?? "admin", ct);
        TempData["Toast.Success"] = approve ? $"Potvrđeno: {changed}." : $"Odbijeno / uklonjeno: {changed}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWeightAsync(string countryCode, decimal? minWeightT, CancellationToken ct)
    {
        if (minWeightT is < 0 or > 60)
        {
            TempData["Toast.Error"] = "Granica mora biti između 0 i 60 t.";
            return RedirectToPage();
        }

        await repository.SetCountryWeightAsync(countryCode, minWeightT, ct);
        TempData["Toast.Success"] = $"Granica za {countryCode} sačuvana.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken ct)
    {
        var result = await sync.SyncAsync(ct);
        TempData[result.Error is null ? "Toast.Success" : "Toast.Error"] = result.Error is null
            ? $"Preuzeto {result.Bans} zabrana za {result.Days} dana; novih: {result.New}, uklonjenih na izvoru: {result.RemovedAtSource}."
            : $"Preuzimanje nije uspelo: {result.Error}";
        return RedirectToPage();
    }

    public static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}
