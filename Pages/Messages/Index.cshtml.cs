using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProMapCargo.Api.Pages.Messages;

/// <summary>Dispatcher chat with drivers; data and live updates come from api/chat and NavigationHub.</summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
