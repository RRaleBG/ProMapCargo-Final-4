using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Pages.Login;

[AllowAnonymous]
public sealed class IndexModel(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public void OnGet(string? returnUrl = null)
    {
        Input.ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        Input.ReturnUrl ??= returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email.Trim()).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Email ili lozinka nisu ispravni.");
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            user.UserName ?? user.Email ?? Input.Email,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true).ConfigureAwait(false);

        if (result.Succeeded)
        {
            TempData["Toast.Success"] = "Uspešna prijava.";

            return Url.IsLocalUrl(Input.ReturnUrl)
                ? LocalRedirect(Input.ReturnUrl!)
                : RedirectToPage("/Index");
        }

        if (result.IsLockedOut)
        {
            return RedirectToPage("/Account/Lockout");
        }

        if (result.RequiresTwoFactor)
        {
            ModelState.AddModelError(string.Empty, "Ovaj nalog zahteva verifikaciju u dva koraka.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Email ili lozinka nisu ispravni.");
        }

        return Page();
    }

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Email adresa je obavezna.")]
        [EmailAddress(ErrorMessage = "Unesite ispravnu email adresu.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Lozinka je obavezna.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
        public string? ReturnUrl { get; set; }
    }
}
