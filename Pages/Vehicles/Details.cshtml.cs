using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Vehicles;

public sealed class DetailsModel(BusinessService business) : InitialDataPageModel
{
    [BindProperty(SupportsGet = true, Name = "vehicleId")]
    public Guid VehicleId { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (VehicleId == Guid.Empty)
        {
            return RedirectToPage("/Vehicles/Index");
        }

        ViewData["PageAssetKey"] = "vehicle-details-page";
        Seed($"/api/business/vehicles/{VehicleId}/details", await OrNull(() => business.VehicleDetailsAsync(VehicleId, ct)));
        return Page();
    }
}
