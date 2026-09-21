using System.Net.Http.Json;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class ApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient.PostAsJsonAsync("api/mobile-auth/login", request, SerializerOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidOperationException("Login response was empty.");
    }

    public async Task<DashboardSummary?> GetDashboardAsync(CancellationToken cancellationToken)
        => await httpClient.GetFromJsonAsync<DashboardSummary>("api/business/dashboard", SerializerOptions, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<VehicleItem>> GetVehiclesAsync(CancellationToken cancellationToken)
        => await httpClient.GetFromJsonAsync<List<VehicleItem>>("api/business/vehicles", SerializerOptions, cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<IReadOnlyList<DriverItem>> GetDriversAsync(CancellationToken cancellationToken)
        => await httpClient.GetFromJsonAsync<List<DriverItem>>("api/business/drivers", SerializerOptions, cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<IReadOnlyList<TransportOrderItem>> GetOrdersAsync(CancellationToken cancellationToken)
        => await httpClient.GetFromJsonAsync<List<TransportOrderItem>>("api/business/orders", SerializerOptions, cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<IReadOnlyList<TripItem>> GetTripsAsync(CancellationToken cancellationToken)
        => await httpClient.GetFromJsonAsync<List<TripItem>>("api/business/trips", SerializerOptions, cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<RouteResponse?> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient.PostAsJsonAsync("api/routing/route", request, SerializerOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RouteResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}
