using System.Globalization;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Text of the "Zabrane" card on the home screen: bans in force now, later today and in
/// the next two days, for this truck (a ban counts above the country's weight limit).
/// Informational only: routing does not use the bans yet. The last calendar is kept on the
/// phone so the card also works without signal.
/// </summary>
public static class TruckBanSummary
{
    public const decimal TruckWeightT = 40m; // the app's truck profile (40 t · 4,0 m · 2,55 m · 16,5 m)

    private static readonly string CachePath = Path.Combine(FileSystem.AppDataDirectory, "truck-bans.json");
    private static readonly CultureInfo Serbian = new("sr-Latn-RS");

    public sealed record Result(string Summary, string Badge, bool ActiveNow, string Details);

    public static void Save(TruckBanCalendar calendar)
    {
        try
        {
            File.WriteAllText(CachePath, JsonSerializer.Serialize(calendar));
        }
        catch (IOException)
        {
        }
    }

    public static TruckBanCalendar? LoadSaved()
    {
        try
        {
            return File.Exists(CachePath) ? JsonSerializer.Deserialize<TruckBanCalendar>(File.ReadAllText(CachePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public static Result Build(TruckBanCalendar? calendar, DateTimeOffset now)
    {
        if (calendar is null)
        {
            return new Result("Zabrane još nisu preuzete sa servera.", "—", false, "Zabrane će se prikazati kad se telefon poveže sa serverom.");
        }

        var countries = calendar.Countries.ToDictionary(c => c.CountryCode);
        var active = new List<string>();
        var today = new List<string>();
        var details = new List<string>();

        foreach (var ban in calendar.Bans.OrderBy(b => b.Date).ThenBy(b => b.Start))
        {
            if (!countries.TryGetValue(ban.CountryCode, out var country) ||
                (country.MinWeightT is { } limit && TruckWeightT <= limit))
            {
                continue;
            }

            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(country.TimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                continue;
            }

            var local = TimeZoneInfo.ConvertTime(now, zone);
            var localDay = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(ban.Date, localDay) < 0 ||
                string.CompareOrdinal(ban.Date, local.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) > 0)
            {
                continue;
            }

            var name = country.NameSr ?? country.CountryName;
            var scope = ban.Regional ? " (deo puteva)" : "";
            var minute = local.Hour * 60 + local.Minute;
            var day = DateTime.ParseExact(ban.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            details.Add($"{day.ToString("ddd dd.MM.", Serbian)} · {name} {ban.Start}–{ban.End}{scope}");

            if (ban.Date == localDay && minute >= Minutes(ban.Start) && minute < Minutes(ban.End))
            {
                active.Add($"{name} do {ban.End}{scope}");
            }
            else if (ban.Date == localDay && minute < Minutes(ban.Start))
            {
                today.Add($"{name} {ban.Start}–{ban.End}{scope}");
            }
        }

        var detailText = details.Count == 0
            ? "Nema potvrđenih zabrana za danas i naredna dva dana."
            : string.Join("\n", details) + "\n\nVreme je lokalno za svaku državu.";

        if (active.Count > 0)
        {
            return new Result("U toku: " + string.Join(", ", active), $"U toku: {active.Count}", true, detailText);
        }

        if (today.Count > 0)
        {
            return new Result("Danas: " + string.Join(", ", today), $"Danas: {today.Count}", false, detailText);
        }

        return details.Count > 0
            ? new Result("Danas nema zabrana. Naredna dva dana: " + details.Count + ".", "Uskoro", false, detailText)
            : new Result("Nema potvrđenih zabrana za danas i naredna dva dana.", "Nema", false, detailText);
    }

    private static int Minutes(string clock)
    {
        var parts = clock.Split(':');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 60 + int.Parse(parts[1], CultureInfo.InvariantCulture);
    }
}
