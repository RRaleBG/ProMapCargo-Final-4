using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Services;

public sealed class NominatimGeocodingService( HttpClient http, IConfiguration config) : IGeocodingService
{
    public async Task<IReadOnlyList<object>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var baseUrl =  config["Geocoding:NominatimBaseUrl"];

        if ( string.IsNullOrWhiteSpace(baseUrl)       )
        {
            throw new InvalidOperationException("Geocoding:NominatimBaseUrl nije konfigurisan.");
        }

        baseUrl = baseUrl.TrimEnd('/');

        var url =
            $"{baseUrl}/search" +
            $"?format=jsonv2" +
            $"&q={Uri.EscapeDataString(query)}" +
            $"&limit={Math.Clamp(limit, 1, 10)}" +
            "&addressdetails=1";

        using var request = new HttpRequestMessage(HttpMethod.Get,  url);

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd(config["Geocoding:UserAgent"] ?? "ProMapCargo/1.0");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<List<NominatimPlace>>(cancellationToken: ct) ?? [];
        return data
            .Where(
                item =>
                    double.TryParse(
                        item.Lat,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out _
                    ) &&
                    double.TryParse(
                        item.Lon,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out _
                    )
            )
            .Select(
                item =>
                    (object)new GeocodeResult
                    {
                        Lat = double.Parse(item.Lat, CultureInfo.InvariantCulture),
                        Lon = double.Parse(item.Lon, CultureInfo.InvariantCulture),
                        DisplayName = item.DisplayName,
                        Type = item.Type,
                        Class = item.Class
                    }
            )
            .ToList();
    }

    private sealed class NominatimPlace
    {
        [JsonPropertyName("lat")]
        public string Lat { get; init; } = "";

        [JsonPropertyName("lon")]
        public string Lon { get; init; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; init; } = "";

        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("class")]
        public string Class { get; init; } = "";
    }

    private sealed class GeocodeResult
    {
        [JsonPropertyName("lat")]
        public double Lat { get; init; }

        [JsonPropertyName("lon")]
        public double Lon { get; init; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; init; } = "";

        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("class")]
        public string Class { get; init; } = "";
    }
}