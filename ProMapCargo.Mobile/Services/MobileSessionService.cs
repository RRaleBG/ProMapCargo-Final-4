using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class MobileSessionService(TokenStore tokenStore, ApiClient apiClient)
{
    public LoginResponse? CurrentSession { get; private set; }

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        CurrentSession = await tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
        return CurrentSession is not null && CurrentSession.ExpiresAt > DateTimeOffset.UtcNow;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var response = await apiClient.LoginAsync(new LoginRequest(email, password), cancellationToken).ConfigureAwait(false);
        CurrentSession = response;
        await tokenStore.SaveAsync(response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    public async Task LogoutAsync()
    {
        CurrentSession = null;
        await tokenStore.ClearAsync().ConfigureAwait(false);
    }
}
