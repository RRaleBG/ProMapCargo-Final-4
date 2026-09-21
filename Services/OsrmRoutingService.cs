using System.Globalization;
using System.Text.Json;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class OsrmRoutingService(
    HttpClient http,
    IConfiguration config
) : IRoutingService
{
    public async Task<OsrmResponse> RouteAsync(
        RouteRequest request,
        CancellationToken ct)
    {
        var profile =
            request.Profile?.Trim().ToLowerInvariant()
            switch
            {
                "cycling" => "bike",
                "walking" => "foot",
                _ => "driving"
            };

        var baseUrl =
            config["Routing:OsrmBaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Routing:OsrmBaseUrl nije konfigurisan."
            );
        }

        baseUrl =
            baseUrl.TrimEnd('/');

        var target =
            request.Target;

        var startLon =
            request.Start.Lon.ToString(
                CultureInfo.InvariantCulture
            );

        var startLat =
            request.Start.Lat.ToString(
                CultureInfo.InvariantCulture
            );

        var endLon =
            target.Lon.ToString(
                CultureInfo.InvariantCulture
            );

        var endLat =
            target.Lat.ToString(
                CultureInfo.InvariantCulture
            );

        var coordinates =
            $"{startLon},{startLat};{endLon},{endLat}";

        var url =
            $"{baseUrl}/route/v1/{profile}/{coordinates}" +
            "?overview=full" +
            "&geometries=geojson" +
            "&steps=true" +
            "&alternatives=true";

        using var response =
            await http.GetAsync(
                url,
                ct
            );

        var content =
            await response.Content.ReadAsStringAsync(
                ct
            );

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OSRM HTTP {(int)response.StatusCode}: {content}"
            );
        }

        var result =
            JsonSerializer.Deserialize<OsrmResponse>(
                content,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web
                )
            );

        if (result is null)
        {
            throw new InvalidOperationException(
                "OSRM je vratio prazan ili nevalidan odgovor."
            );
        }

        return result;
    }
}