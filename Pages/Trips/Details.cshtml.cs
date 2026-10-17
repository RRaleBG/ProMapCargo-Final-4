using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Trips;

public sealed class DetailsModel(BusinessService business) : InitialDataPageModel
{
    [BindProperty(SupportsGet = true, Name = "tripId")]
    public Guid TripId { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (TripId == Guid.Empty)
        {
            return RedirectToPage("/Trips/Index");
        }

        ViewData["PageAssetKey"] = "trip-details-page";
        Seed($"/api/business/trips/{TripId}/details", await OrNull(() => business.TripDetailsAsync(TripId, ct)));
        return Page();
    }
}
