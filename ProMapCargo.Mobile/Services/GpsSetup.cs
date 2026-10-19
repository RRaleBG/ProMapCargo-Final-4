namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Makes sure the GPS works from the first start: explains why the app needs the location,
/// asks for the permissions (location, notifications for the tracking service), checks that
/// location is switched on in the phone and takes a first fix so the GPS is warm when the
/// driver opens navigation. Runs at every start; asks only for what is missing.
/// </summary>
public sealed class GpsSetup(IMobileNotifier notifier)
{
    private const string ExplainedKey = "setup.gps_explained";

    public async Task EnsureReadyAsync()
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>).ConfigureAwait(false);
            if (status != PermissionStatus.Granted)
            {
                if (!Preferences.Default.Get(ExplainedKey, false))
                {
                    Preferences.Default.Set(ExplainedKey, true);
                    await AlertAsync(
                        "Lokacija za navigaciju",
                        "ProMap Cargo koristi GPS za navigaciju kamiona, automatski dolazak na stanicu i poziciju za dispečera dok je tura aktivna. " +
                        "Na sledećem pitanju izaberite „Dok se aplikacija koristi“.").ConfigureAwait(false);
                }

                status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.LocationWhenInUse>).ConfigureAwait(false);
                if (status != PermissionStatus.Granted)
                {
                    await notifier.ShowWarningAsync("Bez dozvole za lokaciju navigacija i praćenje ture ne rade. Uključite je u Podešavanjima.").ConfigureAwait(false);
                    return;
                }
            }

            // Android 13+: the notification of the tracking service (GPS with the screen off) and messages.
            if (DeviceInfo.Platform == DevicePlatform.Android && OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                    {
                        await Permissions.RequestAsync<Permissions.PostNotifications>();
                    }
                }).ConfigureAwait(false);
            }

            if (!IsLocationSwitchedOn())
            {
                var open = await MainThread.InvokeOnMainThreadAsync(() => Shell.Current is { } shell
                    ? shell.DisplayAlertAsync("Lokacija je isključena", "Uključite lokaciju (GPS) na telefonu da bi navigacija radila.", "Otvori podešavanja", "Kasnije")
                    : Task.FromResult(false)).ConfigureAwait(false);
                if (open)
                {
                    OpenLocationSettings();
                }

                return;
            }

            // First fix in the background: the receiver is warm when navigation opens.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _ = await MainThread.InvokeOnMainThreadAsync(() =>
                Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(15)), timeout.Token)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPS setup: {ex.Message}");
        }
    }

    private static Task AlertAsync(string title, string text)
        => MainThread.InvokeOnMainThreadAsync(() => Shell.Current is { } shell
            ? shell.DisplayAlertAsync(title, text, "U redu")
            : Task.CompletedTask);

    private static bool IsLocationSwitchedOn()
    {
#if ANDROID
        var manager = Platform.AppContext.GetSystemService(Android.Content.Context.LocationService) as Android.Locations.LocationManager;
        return manager is null || (OperatingSystem.IsAndroidVersionAtLeast(28)
            ? manager.IsLocationEnabled
            : manager.IsProviderEnabled(Android.Locations.LocationManager.GpsProvider));
#else
        return true;
#endif
    }

    private static void OpenLocationSettings()
    {
#if ANDROID
        var intent = new Android.Content.Intent(Android.Provider.Settings.ActionLocationSourceSettings);
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(intent);
#endif
    }
}
