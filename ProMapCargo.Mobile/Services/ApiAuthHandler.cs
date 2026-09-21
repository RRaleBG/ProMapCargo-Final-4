using System.Net.Http.Headers;

namespace ProMapCargo.Mobile.Services;

public sealed class ApiAuthHandler(TokenStore tokenStore) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(session?.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
