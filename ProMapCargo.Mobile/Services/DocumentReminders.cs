using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Reminds the driver of documents that run out (licence, CPC, tachograph card, medical,
/// ADR): a phone notification 30 days, 7 days and 1 day before, and when it has expired.
/// Each step once per document; checked at most once a day, from the driver's profile.
/// </summary>
public sealed class DocumentReminders(ApiClient apiClient, DriverDataCache cache, MobileSessionService session)
{
    private const string ProfileCacheKey = "profile";
    private const string LastCheckKey = "documents.last_check";
    private static readonly int[] Steps = [30, 7, 1, 0];

    public async Task CheckAsync()
    {
        if (session.CurrentSession?.User.Id is not { } userId ||
            Preferences.Default.Get(LastCheckKey, string.Empty) == DateTime.Today.ToString("yyyy-MM-dd"))
        {
            return;
        }

        DriverProfile? profile;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            profile = await apiClient.GetDriverProfileAsync(timeout.Token).ConfigureAwait(false);
            if (profile is not null)
            {
                cache.Save(ProfileCacheKey, profile, userId);
            }
        }
        catch (Exception)
        {
            // Not a driver, or no signal: the saved profile (if any).
            profile = cache.Load<DriverProfile>(ProfileCacheKey, userId)?.Value;
        }

        if (profile is null)
        {
            return;
        }

        Preferences.Default.Set(LastCheckKey, DateTime.Today.ToString("yyyy-MM-dd"));
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var (key, title, expiry) in new (string, string, DateOnly?)[]
                 {
                     ("license", "Vozačka dozvola", profile.LicenseExpiry),
                     ("cpc", "CPC (Kod 95)", profile.Code95Expiry),
                     ("tacho", "Tahograf kartica", profile.TachographCardExpiry),
                     ("medical", "Lekarsko uverenje", profile.MedicalExpiry),
                     ("adr", "ADR sertifikat", profile.AdrExpiry),
                 })
        {
            if (expiry is not { } date)
            {
                continue;
            }

            var days = date.DayNumber - today.DayNumber;
            // The smallest step already reached (30 → 7 → 1 → 0 = expired), or none yet.
            var step = Steps.Where(limit => days <= limit).DefaultIfEmpty(-1).Min();
            if (step < 0)
            {
                continue;
            }

            // Per document and expiry date: a renewed document starts over.
            var shownKey = $"documents.{key}.{date:yyyyMMdd}";
            if (Preferences.Default.Get(shownKey, int.MaxValue) <= step)
            {
                continue;
            }

            Preferences.Default.Set(shownKey, step);
            var text = days < 0
                ? $"{title} je istekla {date:dd.MM.yyyy.}. Obavestite dispečera."
                : days == 0
                    ? $"{title} ističe danas."
                    : $"{title} ističe za {days} {(days == 1 ? "dan" : "dana")} ({date:dd.MM.yyyy.}).";
            LocalNotifications.Show(key.GetHashCode() & 0x7FFF, days < 0 ? "Dokument je istekao" : "Dokument ističe", text);
        }
    }
}
