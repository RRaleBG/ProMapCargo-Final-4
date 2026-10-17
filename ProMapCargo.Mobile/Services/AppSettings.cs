namespace ProMapCargo.Mobile.Services;

/// <summary>Driver settings (Podešavanja), stored on the phone.</summary>
public static class AppSettings
{
    private const string ShareLocationKey = "settings.share_location";
    private const string KeepScreenOnKey = "settings.keep_screen_on";
    private const string VoiceGuidanceKey = "settings.voice_guidance";

    /// <summary>Send the truck's position to dispatch while a trip is running.</summary>
    public static bool ShareLocation
    {
        get => Preferences.Default.Get(ShareLocationKey, true);
        set => Preferences.Default.Set(ShareLocationKey, value);
    }

    /// <summary>The screen stays on on the navigation screen.</summary>
    public static bool KeepScreenOn
    {
        get => Preferences.Default.Get(KeepScreenOnKey, true);
        set => Preferences.Default.Set(KeepScreenOnKey, value);
    }

    /// <summary>Spoken maneuvers when navigation opens.</summary>
    public static bool VoiceGuidance
    {
        get => Preferences.Default.Get(VoiceGuidanceKey, true);
        set => Preferences.Default.Set(VoiceGuidanceKey, value);
    }
}
