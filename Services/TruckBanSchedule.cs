namespace ProMapCargo.Api.Services;

/// <summary>
/// Approved truck bans as UTC intervals for the router (yesterday to three days ahead,
/// reloaded every 10 minutes). Local ban times are converted in each country's time zone;
/// a ban applies to a truck heavier than the country's limit (no limit set: every truck).
/// </summary>
public sealed class TruckBanSchedule(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<TruckBanSchedule> logger)
{
    /// <summary>
    /// Whether the router avoids banned countries. Off by default: bans are shown (map,
    /// app home screen) but do not change routes until this is switched on.
    /// </summary>
    public bool ApplyToRouting => configuration.GetValue("TruckBans:ApplyToRouting", false);

    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim gate = new(1, 1);
    private Snapshot current = Snapshot.Empty;
    private DateTime loadedAtUtc = DateTime.MinValue;

    public sealed record Interval(string CountryCode, string CountryName, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string LocalEnd, decimal? MinWeightT);

    public sealed class Snapshot(IReadOnlyList<Interval> nationwide)
    {
        public static readonly Snapshot Empty = new([]);

        private readonly ILookup<string, Interval> byCountry = nationwide.ToLookup(i => i.CountryCode);

        public IReadOnlyList<Interval> Nationwide { get; } = nationwide;

        /// <summary>Any nationwide ban for this truck in [from, to)?</summary>
        public bool Any(DateTimeOffset from, DateTimeOffset to, decimal weightT) =>
            Nationwide.Any(i => Applies(i, weightT) && i.StartUtc < to && i.EndUtc > from);

        /// <summary>The nationwide ban of a country in force at a moment for this truck, if any.</summary>
        public Interval? At(string countryCode, DateTimeOffset moment, decimal weightT) =>
            byCountry[countryCode].FirstOrDefault(i => Applies(i, weightT) && i.StartUtc <= moment && moment < i.EndUtc);

        private static bool Applies(Interval i, decimal weightT) => i.MinWeightT is not { } limit || weightT > limit;
    }

    public async Task<Snapshot> GetAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - loadedAtUtc < RefreshEvery)
        {
            return current;
        }

        await gate.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - loadedAtUtc >= RefreshEvery)
            {
                current = await LoadAsync(ct);
                loadedAtUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the previous schedule; try again on the next request.
            logger.LogWarning(ex, "Truck ban schedule not refreshed.");
        }
        finally
        {
            gate.Release();
        }

        return current;
    }

    private async Task<Snapshot> LoadAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<TruckBanRepository>();
        var today = DateTime.UtcNow.Date;
        var bans = await repository.GetApprovedAsync(today.AddDays(-1), today.AddDays(3), ct);
        var countries = (await repository.GetCountriesAsync(ct)).ToDictionary(c => c.CountryCode);

        var intervals = new List<Interval>();
        foreach (var ban in bans.Where(b => !b.Regional))
        {
            var zoneId = countries.TryGetValue(ban.CountryCode, out var country)
                ? country.TimeZone
                : TruckBanCountries.ByName.Values.FirstOrDefault(v => v.Code == ban.CountryCode).TimeZone;
            if (zoneId is null)
            {
                continue;
            }

            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            var day = DateTime.SpecifyKind(ban.BanDate.Date, DateTimeKind.Unspecified);
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.AddMinutes(ban.StartMinute), zone));
            var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.AddMinutes(ban.EndMinute), zone));
            intervals.Add(new Interval(ban.CountryCode, country?.CountryName ?? ban.CountryName, start, end,
                $"{ban.EndMinute / 60:00}:{ban.EndMinute % 60:00}", country?.MinWeightT));
        }

        return new Snapshot(intervals);
    }
}
