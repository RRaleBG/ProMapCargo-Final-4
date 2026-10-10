using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class MobileSessionService(TokenStore tokenStore, ApiClient apiClient)
{
    public LoginResponse? CurrentSession { get; private set; }

    public bool IsSessionValid =>
        CurrentSession is not null && CurrentSession.ExpiresAt > DateTimeOffset.UtcNow;

    public string LastStatus { get; private set; } = "Sesija nije pokrenuta.";

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        CurrentSession = await tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentSession is null)
        {
            LastStatus = "Nema sačuvane sesije.";
            return false;
        }

        if (CurrentSession.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            CurrentSession = null;
            await tokenStore.ClearAsync().ConfigureAwait(false);
            LastStatus = "Sačuvana sesija je istekla.";
            return false;
        }

        LastStatus = "Sesija je vraćena.";
        return true;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        var response = await apiClient.LoginAsync(new LoginRequest(email, password), cancellationToken).ConfigureAwait(false);
        CurrentSession = response;
        await tokenStore.SaveAsync(response, cancellationToken).ConfigureAwait(false);
        LastStatus = "Prijava je uspela.";
        return response;
    }

    public async Task LogoutAsync()
    {
        CurrentSession = null;
        await tokenStore.ClearAsync().ConfigureAwait(false);
        LastStatus = "Odjavljeni ste.";
    }
}
