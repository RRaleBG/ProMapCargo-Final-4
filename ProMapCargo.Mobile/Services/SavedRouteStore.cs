using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// The last calculated route, kept on the phone so guidance continues through dead
/// zones and after the app restarts without a connection.
/// </summary>
public sealed class SavedRouteStore
{
    // A planned trip is usually driven the same or the next day.
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(3);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string filePath = Path.Combine(FileSystem.AppDataDirectory, "saved-route.json");

    public void Save(SavedRoute route)
    {
        try
        {
            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(route, SerializerOptions));
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving route failed: {ex.Message}");
        }
    }

    /// <summary>Forgets the planned trip (route reset by the driver).</summary>
    public void Clear()
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
        }
    }

    public SavedRoute? Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var route = JsonSerializer.Deserialize<SavedRoute>(File.ReadAllText(filePath), SerializerOptions);
            return route is not null && DateTime.UtcNow - route.SavedAtUtc <= MaxAge ? route : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public sealed record SavedRoute(
    RouteResponse Response,
    double StartLat,
    double StartLon,
    double DestinationLat,
    double DestinationLon,
    string DestinationTitle,
    DateTime SavedAtUtc);
