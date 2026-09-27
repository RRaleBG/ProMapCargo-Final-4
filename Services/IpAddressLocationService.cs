using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace ProMapCargo.Api.Services;

public interface IIPAddressLocationService
{
    Task<IpAddressLocation?> ResolveAsync(string? ipAddress, CancellationToken cancellationToken);
}

public sealed record IpAddressLocation(string? City, string? Region, string? Country);

public sealed class IpAddressLocationService(HttpClient httpClient, ILogger<IpAddressLocationService> logger) : IIPAddressLocationService
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public async Task<IpAddressLocation?> ResolveAsync(string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var normalizedIp = NormalizeIp(ipAddress);
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            return null;
        }

        if (Cache.TryGetValue(normalizedIp, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Value;
        }

        if (!IPAddress.TryParse(normalizedIp, out var parsedIp) || IsPrivateOrLoopback(parsedIp))
        {
            return null;
        }

        try
        {
            using var response = await httpClient.GetAsync($"http://ip-api.com/json/{normalizedIp}?fields=status,country,regionName,city", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<IpApiResponse>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!string.Equals(payload?.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var location = new IpAddressLocation(payload.City, payload.RegionName, payload.Country);
            Cache[normalizedIp] = new CacheEntry(DateTimeOffset.UtcNow.Add(CacheTtl), location);
            return location;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "IP geolocation request failed for IP {Ip}", normalizedIp);
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "IP geolocation parsing failed for IP {Ip}", normalizedIp);
            return null;
        }
    }

    private static string? NormalizeIp(string rawIp)
    {
        var ip = rawIp.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return ip;
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        return bytes[0] switch
        {
            10 => true,
            127 => true,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }

    private sealed record CacheEntry(DateTimeOffset ExpiresAt, IpAddressLocation Value);

    private sealed class IpApiResponse
    {
        public string? Status { get; set; }
        public string? Country { get; set; }
        public string? RegionName { get; set; }
        public string? City { get; set; }
    }
}
