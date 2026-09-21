using System.Text.Json;
using Microsoft.Maui.Storage;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class TokenStore
{
    private const string SessionKey = "promap-mobile-session";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(LoginResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.Serialize(response, SerializerOptions);
        await SecureStorage.Default.SetAsync(SessionKey, payload).ConfigureAwait(false);
    }

    public async Task<LoginResponse?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = await SecureStorage.Default.GetAsync(SessionKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<LoginResponse>(payload, SerializerOptions);
    }

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(SessionKey);
        return Task.CompletedTask;
    }
}
