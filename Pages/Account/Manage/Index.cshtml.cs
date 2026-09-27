using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Pages.Account.Manage;

//[Authorize]
public sealed class IndexModel(UserManager<ApplicationUser> userManager) : PageModel
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

        var uploadDirectory = Path.Combine(
            HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath,
            "uploads",
            "profiles");
        Directory.CreateDirectory(uploadDirectory);

        var fileName = $"{user.Id:N}-{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadDirectory, fileName);
        await using (var stream = System.IO.File.Create(filePath))
        {
            await ProfileImage.CopyToAsync(stream);
        }

        user.ProfileImageUrl = $"/uploads/profiles/{fileName}";
        await userManager.UpdateAsync(user);
        TempData["Toast.Success"] = "Profilna slika je sačuvana.";
        return RedirectToPage();
    }
}
