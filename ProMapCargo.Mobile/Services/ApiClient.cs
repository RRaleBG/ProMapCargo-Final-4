using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class ApiClient(HttpClient httpClient, MobileServerEndpointService serverEndpointService)
{
    private const int MaxAttempts = 2;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

    // The server may need a long time for the first route through an area whose graph
    // tiles are not cached yet; retrying would only start the same search twice.
    private static readonly TimeSpan RouteRequestTimeout = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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
            requireBody: false,
            timeout: RouteRequestTimeout,
            maxAttempts: 1).ConfigureAwait(false);
    }

    /// <summary>
    /// Quick check whether the server answers at all. Without signal a connection
    /// attempt can hang until the (long) route timeout; this gives up after a few seconds.
    /// </summary>
    public async Task<bool> IsServerReachableAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            using var response = await httpClient.GetAsync(serverEndpointService.CreateUri("api/map-packages"), HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<TruckBanCalendar?> GetTruckBansAsync(CancellationToken cancellationToken)
        => await GetAsync<TruckBanCalendar>("api/truck-bans", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<MapPackageCatalogItem>> GetMapPackagesAsync(CancellationToken cancellationToken)
        => await GetAsync<List<MapPackageCatalogItem>>("api/map-packages", cancellationToken).ConfigureAwait(false)
           ?? [];

    public async Task<IReadOnlyList<GeocodeResult>> GeocodeAsync(string query, CancellationToken cancellationToken)
        => await GetAsync<List<GeocodeResult>>($"api/geocode?q={Uri.EscapeDataString(query)}&limit=5", cancellationToken).ConfigureAwait(false)
           ?? [];

    // ---- Driver (/api/driver, DriverController) ----

    /// <summary>
    /// The signed-in driver's trip and stops. Throws HttpRequestException 404 when the
    /// account is not linked to a driver, 401/403 when not signed in as a driver.
    /// </summary>
    public async Task<DriverPortal?> GetDriverPortalAsync(CancellationToken cancellationToken)
        => await GetAsync<DriverPortal>("api/driver/portal", cancellationToken).ConfigureAwait(false);

    public Task ArriveAtStopAsync(Guid stopId, CancellationToken cancellationToken)
        => SendDriverActionAsync($"api/driver/stops/{stopId}/arrive", null, cancellationToken);

    public Task CompleteStopAsync(Guid stopId, CancellationToken cancellationToken)
        => SendDriverActionAsync($"api/driver/stops/{stopId}/complete", null, cancellationToken);

    /// <summary>Starts the break, or ends it when the trip is paused.</summary>
    public Task TogglePauseAsync(CancellationToken cancellationToken)
        => SendDriverActionAsync("api/driver/pause", null, cancellationToken);

    public Task StartTripAsync(Guid tripId, CancellationToken cancellationToken)
        => SendDriverActionAsync($"api/driver/trips/{tripId}/start", null, cancellationToken);

    public Task AcceptRouteAsync(Guid dispatchId, CancellationToken cancellationToken)
        => SendDriverActionAsync($"api/driver/routes/{dispatchId}/accept", null, cancellationToken);

    public Task RejectRouteAsync(Guid dispatchId, string? reason, CancellationToken cancellationToken)
        => SendDriverActionAsync($"api/driver/routes/{dispatchId}/reject", new DriverRejectRouteRequest(reason), cancellationToken);

    public Task ReportIncidentAsync(string message, string severity, CancellationToken cancellationToken)
        => SendDriverActionAsync("api/driver/incident", new DriverIncidentRequest(message, severity), cancellationToken);

    /// <summary>One GPS position of the driver's vehicle (NavigationTelemetryController).</summary>
    public Task SendGpsPositionAsync(GpsPositionRequest position, CancellationToken cancellationToken)
        => SendDriverActionAsync("api/navigation/gps", position, cancellationToken);

    // Driver actions are not idempotent (a second "arrive" is a conflict): one attempt only.
    private async Task SendDriverActionAsync(string uri, object? body, CancellationToken cancellationToken)
        => await PostAsync<object?, System.Text.Json.JsonElement>(uri, body, cancellationToken, requireBody: false, maxAttempts: 1).ConfigureAwait(false);

    /// <summary>The server's { "message": "..." } of a failed request, else the HTTP status.</summary>
    public static string DescribeError(Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: { } status } httpException)
        {
            var payload = httpException.Message;
            var start = payload.IndexOf('{');
            if (start >= 0)
            {
                try
                {
                    using var document = JsonDocument.Parse(payload[start..]);
                    if (document.RootElement.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
                    {
                        return text;
                    }
                }
                catch (JsonException)
                {
                }
            }

            return status switch
            {
                HttpStatusCode.Unauthorized => "Prijavite se ponovo.",
                HttpStatusCode.Forbidden => "Nalog nema pravo na ovu radnju.",
                HttpStatusCode.NotFound => "Podatak nije pronađen na serveru.",
                _ => $"Server je odgovorio greškom {(int)status}.",
            };
        }

        return exception is OperationCanceledException or HttpRequestException
            ? "Nema veze sa serverom."
            : exception.Message;
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string uri,
        TRequest request,
        CancellationToken cancellationToken,
        bool requireBody,
        TimeSpan? timeout = null,
        int? maxAttempts = null)
    {
        return await ExecuteWithRetryAsync(async token =>
        {
            using var response = await httpClient.PostAsJsonAsync(serverEndpointService.CreateUri(uri), request, SerializerOptions, token).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpRequestException(response.StatusCode, payload);
            }

            // Some actions answer 200 without a body (e.g. the GPS endpoint).
            var result = string.IsNullOrWhiteSpace(payload)
                ? default
                : JsonSerializer.Deserialize<TResponse>(payload, SerializerOptions);
            if (requireBody && result is null)
            {
                throw new InvalidOperationException($"Response body was empty for '{uri}'.");
            }

            return result;
        }, cancellationToken, timeout, maxAttempts).ConfigureAwait(false);
    }

    private async Task<T?> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async token =>
        {
            using var response = await httpClient.GetAsync(serverEndpointService.CreateUri(uri), token).ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        int? maxAttempts = null)
    {
        var attempts = maxAttempts ?? MaxAttempts;

        for (var attempt = 1; ; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout ?? RequestTimeout);

            try
            {
                return await operation(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < attempts && IsTransient(ex, cancellationToken))
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
