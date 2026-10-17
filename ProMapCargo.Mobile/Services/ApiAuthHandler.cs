using System.Net;
using System.Net.Http.Headers;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Adds the driver's access token to every API request. A token about to expire is
/// renewed first; when the server still answers 401 the token is renewed once and the
/// request is sent again, so an expired token never surfaces as an error.
/// </summary>
public sealed class ApiAuthHandler(MobileSessionService sessionService) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await sessionService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // The body is sent twice when the request is retried.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var renewed = await sessionService.RefreshAsync(token, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(renewed))
        {
            return response;
        }

        response.Dispose();
        // Not disposed here: the response keeps a reference to its request.
        var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    // An HttpRequestMessage can be sent only once.
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
