using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class ApiClient(HttpClient httpClient)
{
    private const int MaxAttempts = 2;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await PostAsync<LoginRequest, LoginResponse>(
            "api/mobile-auth/login",
            request,
            cancellationToken,
            requireBody: true).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Login response was empty.");
    }

    public async Task<DashboardSummary?> GetDashboardAsync(CancellationToken cancellationToken)
        => await GetAsync<DashboardSummary>("api/business/dashboard", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<VehicleItem>> GetVehiclesAsync(CancellationToken cancellationToken)
        => await GetAsync<List<VehicleItem>>("api/business/vehicles", cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<IReadOnlyList<DriverItem>> GetDriversAsync(CancellationToken cancellationToken)
        => await GetAsync<List<DriverItem>>("api/business/drivers", cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<IReadOnlyList<TransportOrderItem>> GetOrdersAsync(CancellationToken cancellationToken)
        => await GetAsync<List<TransportOrderItem>>("api/business/orders", cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<IReadOnlyList<TripItem>> GetTripsAsync(CancellationToken cancellationToken)
        => await GetAsync<List<TripItem>>("api/business/trips", cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<RouteResponse?> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await PostAsync<RouteRequest, RouteResponse>(
            "api/routing/route",
            request,
            cancellationToken,
            requireBody: false).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MapPackageCatalogItem>> GetMapPackagesAsync(CancellationToken cancellationToken)
        => await GetAsync<List<MapPackageCatalogItem>>("api/map-packages", cancellationToken).ConfigureAwait(false)
           ?? [];

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string uri,
        TRequest request,
        CancellationToken cancellationToken,
        bool requireBody)
    {
        return await ExecuteWithRetryAsync(async token =>
        {
            using var response = await httpClient.PostAsJsonAsync(uri, request, SerializerOptions, token).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpRequestException(response.StatusCode, payload);
            }

            var result = JsonSerializer.Deserialize<TResponse>(payload, SerializerOptions);
            if (requireBody && result is null)
            {
                throw new InvalidOperationException($"Response body was empty for '{uri}'.");
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async token =>
        {
            using var response = await httpClient.GetAsync(uri, token).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpRequestException(response.StatusCode, payload);
            }

            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestException CreateHttpRequestException(HttpStatusCode statusCode, string payload)
        => new($"HTTP {(int)statusCode}: {payload}", null, statusCode);

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        if (exception is HttpRequestException httpException)
        {
            if (httpException.StatusCode is null)
            {
                return true;
            }

            var statusCode = (int)httpException.StatusCode.Value;
            return statusCode == 408 || statusCode == 429 || statusCode >= 500;
        }

        return false;
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            try
            {
                return await operation(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex, cancellationToken))
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
