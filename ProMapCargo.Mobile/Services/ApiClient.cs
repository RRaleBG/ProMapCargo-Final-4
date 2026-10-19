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

    /// <summary>
    /// Number of items a list endpoint returns. /api/business/* sends the stored entities
    /// (enums as numbers), which the typed list models do not read; a count needs no model.
    /// </summary>
    public async Task<int> CountAsync(string uri, CancellationToken cancellationToken)
        => (await GetAsync<List<JsonElement>>(uri, cancellationToken).ConfigureAwait(false))?.Count ?? 0;

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

    /// <summary>
    /// A stored file (receipt) with the driver's token: the system browser has no token and
    /// gets 401, so the app downloads it and opens the local copy.
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType, string? FileName)> DownloadFileAsync(Guid fileId, CancellationToken cancellationToken)
        => await ExecuteWithRetryAsync(async token =>
        {
            using var response = await httpClient.GetAsync(serverEndpointService.CreateUri($"api/files/{fileId}"), token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpRequestException(response.StatusCode, await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            return (bytes, contentType, fileName);
        }, cancellationToken, TimeSpan.FromSeconds(60)).ConfigureAwait(false);

    /// <summary>The newest app the server offers, or null when it has none published.</summary>
    public async Task<AppReleaseInfo?> GetLatestAppAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(serverEndpointService.CreateUri("api/mobile/app/latest"), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppReleaseInfo>(SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Downloads the app package to <paramref name="targetPath"/>, reporting 0..1.</summary>
    public async Task DownloadAppAsync(string downloadPath, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(serverEndpointService.CreateUri(downloadPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        var tempPath = targetPath + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                if (total > 0)
                {
                    progress?.Report((double)done / total);
                }
            }
        }

        File.Move(tempPath, targetPath, overwrite: true);
    }

    public async Task AddTripCostAsync(DriverExpenseDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await ExecuteWithRetryAsync(async token =>
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(draft.Category), "Category");
            if (draft.TripId is { } tripId) content.Add(new StringContent(tripId.ToString()), "TripId");
            content.Add(new StringContent(draft.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Amount");
            content.Add(new StringContent(draft.IncurredAt.ToString("O")), "IncurredAt");
            if (!string.IsNullOrWhiteSpace(draft.Location)) content.Add(new StringContent(draft.Location), "Location");
            if (!string.IsNullOrWhiteSpace(draft.CountryCode)) content.Add(new StringContent(draft.CountryCode), "CountryCode");
            if (!string.IsNullOrWhiteSpace(draft.PaymentMethod)) content.Add(new StringContent(draft.PaymentMethod), "PaymentMethod");
            if (!string.IsNullOrWhiteSpace(draft.CardLabel)) content.Add(new StringContent(draft.CardLabel), "CardLabel");
            if (draft.Quantity is { } quantity) content.Add(new StringContent(quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Quantity");
            if (draft.UnitPrice is { } unitPrice) content.Add(new StringContent(unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)), "UnitPrice");
            if (draft.OdometerKm is { } odometerKm) content.Add(new StringContent(odometerKm.ToString(System.Globalization.CultureInfo.InvariantCulture)), "OdometerKm");
            if (draft.VehicleId is { } vehicleId) content.Add(new StringContent(vehicleId.ToString()), "VehicleId");
            if (!string.IsNullOrWhiteSpace(draft.Notes)) content.Add(new StringContent(draft.Notes), "Notes");
            content.Add(new StringContent(draft.Currency), "Currency");
            if (draft.ReceiptBytes is { Length: > 0 } receiptBytes)
            {
                var receiptContent = new ByteArrayContent(receiptBytes);
                if (!string.IsNullOrWhiteSpace(draft.ReceiptContentType))
                {
                    receiptContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(draft.ReceiptContentType);
                }
                content.Add(receiptContent, "Receipt", draft.ReceiptFileName ?? "receipt.bin");
            }

            using var response = await httpClient.PostAsync(serverEndpointService.CreateUri("api/finance/costs"), content, token).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpRequestException(response.StatusCode, payload);
            }

            return true;
        }, cancellationToken, maxAttempts: 1).ConfigureAwait(false);
    }

    // ---- Chat with dispatch (ChatController) ----

    public async Task<ChatConversation?> GetChatAsync(CancellationToken cancellationToken)
        => await GetAsync<ChatConversation>("api/chat/me", cancellationToken).ConfigureAwait(false);

    public async Task<ChatMessage?> SendChatAsync(string text, CancellationToken cancellationToken)
        => await PostAsync<object, ChatMessage>("api/chat/me", new { text }, cancellationToken, requireBody: false, maxAttempts: 1).ConfigureAwait(false);

    public Task MarkChatReadAsync(CancellationToken cancellationToken)
        => SendDriverActionAsync("api/chat/me/read", null, cancellationToken);

    /// <summary>Errors of this phone for the administrator (MobileErrorsController).</summary>
    public Task SendErrorReportsAsync(IReadOnlyList<ErrorReport> reports, CancellationToken cancellationToken)
        => SendDriverActionAsync("api/mobile/errors", reports, cancellationToken);

    /// <summary>The driver's messages (alerts, routes from dispatch, own incidents), newest first.</summary>
    public async Task<IReadOnlyList<DriverMessage>> GetDriverMessagesAsync(CancellationToken cancellationToken)
        => await GetAsync<List<DriverMessage>>("api/driver/messages", cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>The driver's record: documents with expiry dates, vehicle, trips done.</summary>
    public async Task<DriverProfile?> GetDriverProfileAsync(CancellationToken cancellationToken)
        => await GetAsync<DriverProfile>("api/driver/profile", cancellationToken).ConfigureAwait(false);

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
