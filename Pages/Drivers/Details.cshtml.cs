using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Drivers;

public sealed class DetailsModel(BusinessService business) : InitialDataPageModel
{
    [BindProperty(SupportsGet = true, Name = "driverId")]
    public Guid DriverId { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (DriverId == Guid.Empty)
        {
            return RedirectToPage("/Drivers/Index");
        }

        ViewData["PageAssetKey"] = "driver-details-page";
        Seed($"/api/business/drivers/{DriverId}/details", await OrNull(() => business.DriverDetailsAsync(DriverId, ct)));
        return Page();
    }
}
