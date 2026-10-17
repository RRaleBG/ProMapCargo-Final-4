using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed record SavedDriverPortal(DriverPortal Portal, DateTimeOffset SavedAt, Guid UserId);

/// <summary>
/// The last trip loaded from the server, so the home screen shows the trip and its stops
/// without signal. Kept per user: another driver signing in on the phone never sees it.
/// </summary>
public sealed class DriverPortalStore
{
    private readonly string path = Path.Combine(FileSystem.AppDataDirectory, "driver-portal.json");

    public SavedDriverPortal? Load(Guid userId)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var saved = JsonSerializer.Deserialize<SavedDriverPortal>(File.ReadAllText(path));
            return saved?.UserId == userId ? saved : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(DriverPortal portal, Guid userId)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new SavedDriverPortal(portal, DateTimeOffset.Now, userId)));
        }
        catch (Exception)
        {
            // Without the copy the trip shows again once the server answers.
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
