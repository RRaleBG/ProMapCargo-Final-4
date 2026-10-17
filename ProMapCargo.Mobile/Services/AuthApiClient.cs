using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Sign-in and token refresh (/api/mobile-auth). A separate client without
/// <see cref="ApiAuthHandler"/>: the handler refreshes through this client, so the
/// two must not depend on each other.
/// </summary>
public sealed class AuthApiClient(HttpClient httpClient, MobileServerEndpointService serverEndpointService)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        => PostAsync("api/mobile-auth/login", request, cancellationToken);

    /// <summary>A new access and refresh token; the old refresh token is revoked by the server.</summary>
    public Task<LoginResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => PostAsync("api/mobile-auth/refresh", new RefreshTokenRequest(refreshToken), cancellationToken);

    private async Task<LoginResponse> PostAsync<TRequest>(string uri, TRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var response = await httpClient
            .PostAsJsonAsync(serverEndpointService.CreateUri(uri), request, SerializerOptions, timeout.Token)
            .ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(DescribeFailure(response.StatusCode, payload), null, response.StatusCode);
        }

        return JsonSerializer.Deserialize<LoginResponse>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("Server nije vratio podatke o prijavi.");
    }

    private static string DescribeFailure(HttpStatusCode statusCode, string payload)
    {
        // Sign-in and refresh answer 401 in English ("Invalid credentials.").
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return "Pogrešna e-pošta ili lozinka.";
        }

        // Other answers carry { "message": "..." }; show that rather than raw JSON.
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return $"Server je odgovorio greškom {(int)statusCode}.";
    }
}
