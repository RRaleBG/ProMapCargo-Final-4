using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

/// <summary>
/// Approved truck driving bans for the map, the router and the phones (which keep the
/// response for offline use). Times are local to the country; "active" evaluates them.
/// </summary>
[ApiController]
[Route("api/truck-bans")]
public sealed class TruckBansController(TruckBanRepository repository) : ControllerBase
{
    /// <summary>Approved bans of a date range (default: today and the next 14 days) and the per-country weight limits.</summary>
    [HttpGet]
    public async Task<ActionResult<TruckBanCalendarDto>> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var start = (from ?? DateTime.UtcNow).Date.AddDays(-1); // a ban window crosses UTC midnight
        var end = (to ?? DateTime.UtcNow.AddDays(14)).Date;
        if (end < start || (end - start).TotalDays > 62)
        {
            return BadRequest(new { message = "Opseg datuma mora biti do 62 dana." });
        }

        var bans = await repository.GetApprovedAsync(start, end, ct);
        // Every calendar country (time zone from the static list), with the weight limit
        // where an administrator has set one.
        var limits = (await repository.GetCountriesAsync(ct)).ToDictionary(c => c.CountryCode);
        var countries = TruckBanCountries.ByName
            .Select(pair => new TruckBanCountryDto(
                pair.Value.Code,
                pair.Key,
                pair.Value.TimeZone,
                limits.TryGetValue(pair.Value.Code, out var limit) ? limit.MinWeightT : null,
                TruckBanCountries.SerbianName(pair.Value.Code)))
            .OrderBy(c => c.NameSr)
            .ToList();
        return Ok(new TruckBanCalendarDto(
            DateTime.UtcNow,
            countries,
            bans.Select(b => new TruckBanDto(b.CountryCode, b.CountryName, b.BanDate.ToString("yyyy-MM-dd"), Clock(b.StartMinute), Clock(b.EndMinute), b.Regional, b.Info)).ToList()));
    }

    /// <summary>Countries with an approved nationwide or regional ban at the given moment (default: now).</summary>
    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<TruckBanActiveDto>>> Active([FromQuery] DateTimeOffset? at, CancellationToken ct)
    {
        var moment = at ?? DateTimeOffset.UtcNow;
        var bans = await repository.GetApprovedAsync(moment.UtcDateTime.Date.AddDays(-1), moment.UtcDateTime.Date.AddDays(1), ct);
        var zones = TruckBanCountries.ByName.Values.ToDictionary(v => v.Code, v => v.TimeZone);
        var active = bans
            .Where(b => zones.TryGetValue(b.CountryCode, out var zone) && IsActive(b, zone, moment))
            .Select(b => new TruckBanActiveDto(b.CountryCode, b.CountryName, b.Regional, Clock(b.StartMinute), Clock(b.EndMinute), b.Info))
            .ToList();
        return Ok(active);
    }

    /// <summary>Whether a ban (local date, minutes from local midnight) covers a moment.</summary>
    public static bool IsActive(TruckBan ban, string timeZone, DateTimeOffset moment)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = TimeZoneInfo.ConvertTime(moment, zone);
        if (DateOnly.FromDateTime(local.DateTime) != DateOnly.FromDateTime(ban.BanDate))
        {
            return false;
        }

        var minute = local.Hour * 60 + local.Minute;
        return minute >= ban.StartMinute && minute < ban.EndMinute;
    }

    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    public sealed record TruckBanCalendarDto(DateTime GeneratedAtUtc, IReadOnlyList<TruckBanCountryDto> Countries, IReadOnlyList<TruckBanDto> Bans);

    public sealed record TruckBanCountryDto(string CountryCode, string CountryName, string TimeZone, decimal? MinWeightT, string NameSr);

    public sealed record TruckBanDto(string CountryCode, string CountryName, string Date, string Start, string End, bool Regional, string Info);

    public sealed record TruckBanActiveDto(string CountryCode, string CountryName, bool Regional, string Start, string End, string Info);
}
