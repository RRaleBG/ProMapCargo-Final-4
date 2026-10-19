namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Whether the app is in front, and a signal when the driver comes back to it (screens
/// refresh their data then; a screen left open in the background does not count as seen).
/// </summary>
public static class AppLifecycle
{
    public static bool InForeground { get; private set; } = true;

    /// <summary>The app came back to the front (raised on the main thread).</summary>
    public static event EventHandler? Resumed;

    public static void Sleep() => InForeground = false;

    public static void Resume()
    {
        InForeground = true;
        Resumed?.Invoke(null, EventArgs.Empty);
    }
}
