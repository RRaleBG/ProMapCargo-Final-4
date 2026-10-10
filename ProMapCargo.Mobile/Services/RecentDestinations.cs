using System.Text.Json;

namespace ProMapCargo.Mobile.Services;

public sealed record RecentDestination(string Title, double Latitude, double Longitude, DateTimeOffset UsedAt);

/// <summary>
/// Destinations the driver calculated a route to, newest first (like TomTom's
/// recent destinations). Stored on the device in Preferences.
/// </summary>
public static class RecentDestinations
{
    private const string PreferencesKey = "recent_destinations_v1";
    private const int MaxItems = 5;

    public static IReadOnlyList<RecentDestination> Load()
    {
        try
        {
            var json = Preferences.Default.Get(PreferencesKey, string.Empty);
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<List<RecentDestination>>(json) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Add(string title, double latitude, double longitude)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        // The same place (within ~100 m) moves to the top instead of repeating.
        var items = Load()
            .Where(item => Math.Abs(item.Latitude - latitude) > 0.001 || Math.Abs(item.Longitude - longitude) > 0.001)
            .ToList();

        items.Insert(0, new RecentDestination(title.Trim(), latitude, longitude, DateTimeOffset.Now));

        try
        {
            Preferences.Default.Set(PreferencesKey, JsonSerializer.Serialize(items.Take(MaxItems).ToList()));
        }
        catch (Exception)
        {
            // Not being able to remember a destination must never break navigation.
        }
    }
}
