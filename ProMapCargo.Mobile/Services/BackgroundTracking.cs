namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Keeps GPS fixes coming while the screen is off or another app is open. On Android
/// a foreground service of type "location" with a visible notification is the only way;
/// it is started from the app while it is in front, as Android 12+ requires.
/// Other platforms: nothing to do (the app only targets Android).
/// </summary>
public static class BackgroundTracking
{
    public static void Start()
    {
#if ANDROID
        LocationTrackingService.Start();
#endif
    }

    public static void Stop()
    {
#if ANDROID
        LocationTrackingService.Stop();
#endif
    }
}
