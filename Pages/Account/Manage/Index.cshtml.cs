using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Account.Manage;

[Authorize]
public sealed class IndexModel(UserManager<ApplicationUser> userManager, FileStorageService fileStorageService) : PageModel
{
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp"];
    public ApplicationUser CurrentUser { get; private set; } = null!;
    [BindProperty]
    public IFormFile? ProfileImage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        CurrentUser = user;
        return Page();
    }

    public async Task<IActionResult> OnPostProfileImageAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (ProfileImage is null || ProfileImage.Length is <= 0 or > 5 * 1024 * 1024 ||
            !AllowedImageTypes.Contains(ProfileImage.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            TempData["Toast.Error"] = "Slika mora biti JPG, PNG ili WEBP i manja od 5 MB.";
            return RedirectToPage();
        }

        var extension = Path.GetExtension(ProfileImage.FileName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
        {
            TempData["Toast.Error"] = "Nepodržan format slike.";
            return RedirectToPage();
        }

        if (user.CompanyId is not Guid companyId)
        {
            TempData["Toast.Error"] = "Korisnik nema dodeljenu kompaniju.";
            return RedirectToPage();
        }

        var stored = await fileStorageService.SaveAsync(ProfileImage, companyId, HttpContext.RequestAborted);
        if (stored is null)
        {
            TempData["Toast.Error"] = "Slika nije sačuvana.";
            return RedirectToPage();
        }

        user.ProfileImageUrl = stored.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? $"/api/files/{stored.Id}/inline"
            : $"/api/files/{stored.Id}";
        await userManager.UpdateAsync(user);
        TempData["Toast.Success"] = "Profilna slika je sačuvana.";
        return RedirectToPage();
    }
}
