using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProMapCargo.Api.Pages.Account;

[AllowAnonymous]
public sealed class LockoutModel : PageModel
{
    public void OnGet()
    {
    }
}
