using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Pages.Account;

[Authorize]
public sealed class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
        {
            return Challenge();
        }

        var result = await userManager
            .ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        await signInManager.RefreshSignInAsync(user).ConfigureAwait(false);
        TempData["Toast.Success"] = "Lozinka je uspešno promenjena.";
        return RedirectToPage("/Account/Manage/Index");
    }

    public sealed class ChangePasswordInput
    {
        [Required(ErrorMessage = "Trenutna lozinka je obavezna.")]
        [DataType(DataType.Password)]
        [Display(Name = "Trenutna lozinka")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nova lozinka je obavezna.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Lozinka mora imati najmanje 8 karaktera.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nova lozinka")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Potvrda lozinke je obavezna.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Lozinke se ne poklapaju.")]
        [Display(Name = "Potvrda nove lozinke")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
