using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Pages.Account;

[AllowAnonymous]
public sealed class RegisterModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var displayName = Input.DisplayName.Trim();

        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false)
                       ?? await userManager.FindByNameAsync(email).ConfigureAwait(false);

        if (existing is not null)
        {
            ModelState.AddModelError(string.Empty, "Nalog sa ovom email adresom već postoji.");
            return Page();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await userManager.CreateAsync(user, Input.Password).ConfigureAwait(false);
        if (result.Succeeded)
        {
            TempData["Toast.Success"] = "Nalog je kreiran. Možete se prijaviti.";
            return Redirect("/login");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return Page();
    }

    public sealed class RegisterInput
    {
        [Required(ErrorMessage = "Email adresa je obavezna.")]
        [EmailAddress(ErrorMessage = "Unesite ispravnu email adresu.")]
        [StringLength(256, ErrorMessage = "Email adresa je predugačka.")]
        [Display(Name = "Email adresa")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ime i prezime su obavezni.")]
        [StringLength(120, MinimumLength = 2, ErrorMessage = "Ime mora imati između 2 i 120 karaktera.")]
        [Display(Name = "Ime i prezime")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Lozinka je obavezna.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Lozinka mora imati najmanje 8 karaktera.")]
        [DataType(DataType.Password)]
        [Display(Name = "Lozinka")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Potvrda lozinke je obavezna.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Lozinke se ne poklapaju.")]
        [Display(Name = "Potvrda lozinke")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
