using System.Net;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// The driver's sign-in. The access token is short-lived; it is renewed with the refresh
/// token before it expires and after a 401 (<see cref="ApiAuthHandler"/>). Without signal
/// the saved session stays signed in, so the app (and the GPS queue) keep working offline;
/// only a refresh token rejected by the server ends the session.
/// </summary>
public sealed class MobileSessionService(TokenStore tokenStore, AuthApiClient authApiClient)
{
    // Renew a little before the server would reject the token.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private bool restored;

    public LoginResponse? CurrentSession { get; private set; }

    /// <summary>Signed in on this phone (the access token may be expired while offline).</summary>
    public bool IsSignedIn => CurrentSession is not null;

    public bool IsSessionValid =>
        CurrentSession is not null && CurrentSession.ExpiresAt > DateTimeOffset.UtcNow;

    public bool IsDriver => CurrentSession?.User.Roles.Any(role => string.Equals(role, "Driver", StringComparison.OrdinalIgnoreCase)) == true;

    public string LastStatus { get; private set; } = "Niste prijavljeni.";

    /// <summary>Signed in, signed out, or the server ended the session.</summary>
    public event EventHandler? SessionChanged;

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        if (restored)
        {
            return IsSignedIn;
        }

        try
        {
            CurrentSession = await tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unreadable secure storage (e.g. after a device restore): sign in again.
            CurrentSession = null;
            await tokenStore.ClearAsync().ConfigureAwait(false);
        }

        restored = true;
        LastStatus = CurrentSession is null ? "Niste prijavljeni." : $"Prijavljeni: {CurrentSession.User.DisplayName}";
        return IsSignedIn;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Unesite e-poštu.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Unesite lozinku.", nameof(password));
        }

        var response = await authApiClient.LoginAsync(new LoginRequest(email.Trim(), password), cancellationToken).ConfigureAwait(false);
        await SetSessionAsync(response, cancellationToken).ConfigureAwait(false);
        LastStatus = $"Prijavljeni: {response.User.DisplayName}";
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return response;
    }

    public async Task LogoutAsync()
    {
        CurrentSession = null;
        restored = true;
        await tokenStore.ClearAsync().ConfigureAwait(false);
        LastStatus = "Odjavljeni ste.";
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Access token for a request: renewed first when it is about to expire. Null when not
    /// signed in. Offline the old token is returned (the request fails on the network anyway).
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await RestoreAsync(cancellationToken).ConfigureAwait(false);
        var session = CurrentSession;
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAt - RefreshMargin > DateTimeOffset.UtcNow)
        {
            return session.AccessToken;
        }

        return await RefreshAsync(session.AccessToken, cancellationToken).ConfigureAwait(false) ?? session.AccessToken;
    }

    /// <summary>
    /// New tokens after the server rejected <paramref name="rejectedAccessToken"/> (or it
    /// expired). Requests running in parallel share one refresh. Null when it failed.
    /// </summary>
    public async Task<string?> RefreshAsync(string? rejectedAccessToken, CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = CurrentSession;
            if (session is null)
            {
                return null;
            }

            // Another request already renewed it while this one waited.
            if (!string.Equals(session.AccessToken, rejectedAccessToken, StringComparison.Ordinal) &&
                session.ExpiresAt - RefreshMargin > DateTimeOffset.UtcNow)
            {
                return session.AccessToken;
            }

            try
            {
                var renewed = await authApiClient.RefreshAsync(session.RefreshToken, cancellationToken).ConfigureAwait(false);
                await SetSessionAsync(renewed, cancellationToken).ConfigureAwait(false);
                return renewed.AccessToken;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                // Refresh token expired or revoked (signed in on another phone): sign in again.
                CurrentSession = null;
                await tokenStore.ClearAsync().ConfigureAwait(false);
                LastStatus = "Sesija je istekla. Prijavite se ponovo.";
                SessionChanged?.Invoke(this, EventArgs.Empty);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // No connection: keep the session, renew on the next request.
                return null;
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task SetSessionAsync(LoginResponse response, CancellationToken cancellationToken)
    {
        CurrentSession = response;
        restored = true;
        await tokenStore.SaveAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
