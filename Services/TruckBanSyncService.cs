using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProMapCargo.Api.Services;

/// <summary>
/// Reads the truck ban calendar of truckban.eu once a day (one JSON request: day -> country
/// -> time window, regional flag, affected roads) and, once a week, the "Vehicles" section
/// of each country page (which trucks a ban applies to). Results go to TruckBanRepository
/// for review; nothing is used before an administrator approves it.
/// </summary>
public sealed partial class TruckBanSyncService(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TruckBanSyncService> logger) : BackgroundService
{
    public const string HttpClientName = "truckban";
    private const string CalendarUrl = "https://app.truckban.eu/api/";
    private const string CountryPageUrl = "https://truckban.eu/{0}.html";

    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime lastCountrySyncUtc = DateTime.MinValue;

    public sealed record SyncResult(int Days, int Bans, int New, int RemovedAtSource, int UnknownCountries, string? Error);

    public SyncResult? LastResult { get; private set; }

    public DateTime? LastRunUtc { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("TruckBans:Enabled", true))
        {
            return;
        }

        var interval = TimeSpan.FromHours(configuration.GetValue("TruckBans:SyncHours", 24d));
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // let the app start first
        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            LastResult = await SyncCalendarAsync(ct);
            LastRunUtc = DateTime.UtcNow;
            if (DateTime.UtcNow - lastCountrySyncUtc > TimeSpan.FromDays(7))
            {
                await SyncCountryPagesAsync(ct);
                lastCountrySyncUtc = DateTime.UtcNow;
            }

            return LastResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Truck ban sync failed.");
            LastResult = new SyncResult(0, 0, 0, 0, 0, ex.Message);
            LastRunUtc = DateTime.UtcNow;
            return LastResult;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SyncResult> SyncCalendarAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var http = httpClientFactory.CreateClient(HttpClientName);
        using var document = JsonDocument.Parse(await http.GetStringAsync(CalendarUrl, ct));

        var bans = new List<(string, string, DateTime, int, int, bool, string, string)>();
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var days = new List<DateTime>();
        foreach (var day in document.RootElement.EnumerateObject())
        {
            // "31.10.2026"
            if (!DateTime.TryParseExact(day.Name, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new InvalidDataException($"Unexpected day key '{day.Name}'.");
            }

            days.Add(date);
            foreach (var entry in day.Value.EnumerateObject())
            {
                // A second window of the same country on that day is "Luxembourg2".
                var name = TrailingDigits().Replace(entry.Name, "");
                if (!TruckBanCountries.ByName.TryGetValue(name, out var country))
                {
                    unknown.Add(name);
                    continue;
                }

                var time = entry.Value.TryGetProperty("time", out var t) ? t.GetString() ?? "" : "";
                var match = TimeWindow().Match(time);
                if (!match.Success)
                {
                    throw new InvalidDataException($"Unexpected time '{time}' for {name} on {day.Name}.");
                }

                var start = Minutes(match.Groups[1].Value, match.Groups[2].Value);
                var end = Minutes(match.Groups[3].Value, match.Groups[4].Value);
                var regional = entry.Value.TryGetProperty("regional", out var r) && r.ValueKind == JsonValueKind.True;
                var info = entry.Value.TryGetProperty("info", out var i) ? (i.GetString() ?? "").Trim() : "";
                bans.Add((country.Code, name, date, start, end, regional, info, Md5(info)));
            }
        }

        if (unknown.Count > 0)
        {
            logger.LogWarning("Truck ban calendar: unknown countries skipped: {Countries}", string.Join(", ", unknown));
        }

        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<TruckBanRepository>();
        var inserted = await repository.UpsertSeenAsync(bans, ct);
        var removed = days.Count == 0 ? 0 : await repository.FlagRemovedAsync(days.Min(), days.Max(), started, ct);

        logger.LogInformation(
            "Truck ban calendar synced: {Days} days, {Bans} bans, {New} new (pending review), {Removed} removed at the source.",
            days.Count, bans.Count, inserted, removed);
        return new SyncResult(days.Count, bans.Count, inserted, removed, unknown.Count, null);
    }

    private async Task SyncCountryPagesAsync(CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<TruckBanRepository>();
        foreach (var (name, country) in TruckBanCountries.ByName)
        {
            try
            {
                var page = await http.GetStringAsync(string.Format(CultureInfo.InvariantCulture, CountryPageUrl, Uri.EscapeDataString(name)), ct);
                var section = VehiclesSection().Match(page);
                if (section.Success)
                {
                    var html = Scripts().Replace(section.Groups[1].Value, " "); // inline ad scripts
                    var text = WebUtility.HtmlDecode(Whitespace().Replace(Tags().Replace(html, " "), " ")).Trim();
                    await repository.UpsertCountryTextAsync(country.Code, name, country.TimeZone, text, Md5(text), ct);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Truck ban country page {Country} not read: {Message}", name, ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct); // a few requests a week, spaced out
        }
    }

    private static int Minutes(string hours, string minutes) =>
        int.Parse(hours, CultureInfo.InvariantCulture) * 60 + int.Parse(minutes, CultureInfo.InvariantCulture);

    private static string Md5(string text) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingDigits();

    [GeneratedRegex(@"^\s*(\d{1,2}):(\d{2})\s*-\s*(\d{1,2}):(\d{2})\s*$")]
    private static partial Regex TimeWindow();

    [GeneratedRegex(@"<h1 id=""vehicles"">.*?</h1>(.*?)<h1", RegexOptions.Singleline)]
    private static partial Regex VehiclesSection();

    [GeneratedRegex(@"<script\b.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Scripts();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
