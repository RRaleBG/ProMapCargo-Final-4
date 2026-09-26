using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Pages.Account;

[AllowAnonymous]
public sealed class ResetPasswordModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new();

    public IActionResult OnGet(string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            TempData["Toast.Error"] = "Link za obnovu lozinke nije ispravan ili je nepotpun.";
            return RedirectToPage("/Account/ForgotPassword");
        }

        Input = new ResetPasswordInput
        {
            Email = email,
            Token = token
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email.Trim()).ConfigureAwait(false);

        // Do not reveal that the account does not exist.
        if (user is null || !user.IsActive)
        {
            TempData["Toast.Success"] = "Lozinka je postavljena. Prijavite se novom lozinkom.";
            return RedirectToPage("/Login/Index");
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Token));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "Link za obnovu lozinke nije ispravan.");
            return Page();
        }

        var result = await userManager
            .ResetPasswordAsync(user, decodedToken, Input.Password)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        TempData["Toast.Success"] = "Lozinka je uspešno promenjena. Prijavite se novom lozinkom.";
        return RedirectToPage("/Login/Index");
    }

    public sealed class ResetPasswordInput
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nova lozinka je obavezna.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Lozinka mora imati najmanje 8 karaktera.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nova lozinka")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Potvrda lozinke je obavezna.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Lozinke se ne poklapaju.")]
        [Display(Name = "Potvrda lozinke")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
