using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Moderation;

/// <summary>First load of /moderation, embedded in the page.</summary>
public sealed class IndexModel(ModerationQueries moderation) : InitialDataPageModel
{
    public async Task OnGetAsync(CancellationToken ct)
    {
        Seed("/api/moderation", await moderation.ListAsync(ct));
    }
}
