using System.Text.Json;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Last copy of a driver's server data (messages, profile) on the phone, per user, so the
/// screens show it without signal. Same idea as <see cref="DriverPortalStore"/>.
/// </summary>
public sealed class DriverDataCache
{
    private sealed record Entry<T>(T Value, DateTimeOffset SavedAt, Guid UserId);

    private static string PathFor(string key) => Path.Combine(FileSystem.AppDataDirectory, $"driver-{key}.json");

    public (T Value, DateTimeOffset SavedAt)? Load<T>(string key, Guid userId)
    {
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path))
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<Entry<T>>(File.ReadAllText(path));
            return entry is not null && entry.UserId == userId ? (entry.Value, entry.SavedAt) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save<T>(string key, T value, Guid userId)
    {
        try
        {
            File.WriteAllText(PathFor(key), JsonSerializer.Serialize(new Entry<T>(value, DateTimeOffset.Now, userId)));
        }
        catch (Exception)
        {
            // Without the copy the screen fills again when the server answers.
        }
    }
}
