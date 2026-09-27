using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Pages.Account;

[Authorize]
public sealed class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync().ConfigureAwait(false);
        TempData["Toast.Success"] = "Uspešno ste odjavljeni.";
        return RedirectToPage("/Index");
    }
}
