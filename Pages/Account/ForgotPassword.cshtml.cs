using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Account;

[AllowAnonymous]
public sealed class ForgotPasswordModel(
    UserManager<ApplicationUser> userManager,
    IIdentityEmailSender emailSender) : PageModel
{
    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new();

    public bool Submitted { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var user = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

        // The response is intentionally identical whether or not the account exists,
        // so this form cannot be used to enumerate registered email addresses.
        if (user is not null && user.IsActive)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var resetLink = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { email, token = encodedToken },
                protocol: Request.Scheme);

            if (!string.IsNullOrWhiteSpace(resetLink))
            {
                await emailSender
                    .SendPasswordResetLinkAsync(email, resetLink, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        Submitted = true;
        return Page();
    }

    public sealed class ForgotPasswordInput
    {
        [Required(ErrorMessage = "Email adresa je obavezna.")]
        [EmailAddress(ErrorMessage = "Unesite ispravnu email adresu.")]
        [StringLength(256, ErrorMessage = "Email adresa je predugačka.")]
        [Display(Name = "Email adresa")]
        public string Email { get; set; } = string.Empty;
    }
}
