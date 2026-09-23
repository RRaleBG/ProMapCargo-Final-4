using ProMapCargo.Api.Models;
using System.Globalization;
using System.Text.Json;

namespace ProMapCargo.Api.Services;

public sealed class OsrmRoutingService(HttpClient http, IConfiguration config) : IRoutingService
{
    public async Task<OsrmResponse> RouteAsync(RouteRequest request, CancellationToken ct)
    {
        var profile =
            request.Profile?.Trim().ToLowerInvariant()
            switch
            {
                "cycling" => "bike",
                "walking" => "foot",
                _ => "driving"
            };

        var primaryBaseUrl = config["Routing:OsrmBaseUrl"];
        if (string.IsNullOrWhiteSpace(primaryBaseUrl))
        {
            throw new InvalidOperationException("Routing:OsrmBaseUrl nije konfigurisan.");
        }

        var target = request.Target;
        var startLon = request.Start.Lon.ToString(CultureInfo.InvariantCulture);
        var startLat = request.Start.Lat.ToString(CultureInfo.InvariantCulture);
        var endLon = target.Lon.ToString(CultureInfo.InvariantCulture);
        var endLat = target.Lat.ToString(CultureInfo.InvariantCulture);
        var coordinates = $"{startLon},{startLat};{endLon},{endLat}";

        var baseQuery = new List<string>
        {
            "overview=full",
            "geometries=geojson",
            "steps=false",
            "alternatives=false"
        };

        var truck = request.Truck;

        var primaryQuery = new List<string>(baseQuery);
        if (string.Equals(request.Profile, "truck", StringComparison.OrdinalIgnoreCase) && truck is not null)
        {
            primaryQuery.Add("exclude=ferry");
        }

        var primaryRoutePath = $"route/v1/{profile}/{coordinates}?{string.Join("&", primaryQuery)}";

        return await SendAsync(primaryBaseUrl, primaryRoutePath, ct, TimeSpan.FromSeconds(15));
    }

    private async Task<OsrmResponse> SendAsync(string baseUrl, string routePath, CancellationToken ct, TimeSpan? timeout)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var url = $"{normalizedBaseUrl}/{routePath}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue)
        {
            cts.CancelAfter(timeout.Value);
        }

        using var response = await http.GetAsync(url, cts.Token);
        var content = await response.Content.ReadAsStringAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OSRM HTTP {(int)response.StatusCode}: {content}");
        }

        var result = JsonSerializer.Deserialize<OsrmResponse>(
            content,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        if (result is null)
        {
            throw new InvalidOperationException("OSRM je vratio prazan ili nevalidan odgovor.");
        }

        return result;
    }

}
