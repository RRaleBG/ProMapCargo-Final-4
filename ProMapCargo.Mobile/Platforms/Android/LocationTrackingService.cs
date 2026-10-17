using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace ProMapCargo.Mobile;

/// <summary>
/// Foreground service of type "location": while it runs Android keeps delivering GPS
/// fixes to the app with the screen off (navigation, position for dispatch). It does no
/// work itself; GpsTracker keeps listening. The notification tells the driver why.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public sealed class LocationTrackingService : Service
{
    private const string ChannelId = "promap-tracking";
    private const int NotificationId = 4101;

    public static void Start()
    {
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(LocationTrackingService));
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception ex)
        {
            // E.g. started while the app is in the background (Android 12+): the GPS then
            // runs while the app is open, which is the previous behaviour.
            System.Diagnostics.Debug.WriteLine($"Tracking service not started: {ex.Message}");
        }
    }

    public static void Stop()
    {
        var context = Platform.AppContext;
        context.StopService(new Intent(context, typeof(LocationTrackingService)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeLocation);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
        }
        catch (Exception ex)
        {
            // Location permission withdrawn meanwhile: no service, nothing else breaks.
            System.Diagnostics.Debug.WriteLine($"Tracking service stopped: {ex.Message}");
            StopSelf();
        }

        // Not restarted by the system after the app was closed: GpsTracker starts it again.
        return StartCommandResult.NotSticky;
    }

    private Notification BuildNotification()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26) &&
            GetSystemService(NotificationService) is NotificationManager manager &&
            manager.GetNotificationChannel(ChannelId) is null)
        {
            var channel = new NotificationChannel(ChannelId, "Praćenje lokacije", NotificationImportance.Low)
            {
                Description = "Prikazuje se dok aplikacija koristi GPS za navigaciju i turu.",
            };
            channel.SetShowBadge(false);
            manager.CreateNotificationChannel(channel);
        }

        var openApp = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        var flags = OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent
            : PendingIntentFlags.UpdateCurrent;
        var contentIntent = openApp is null ? null : PendingIntent.GetActivity(this, 0, openApp, flags);

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle("ProMap Cargo");
        builder.SetContentText("Lokacija se koristi za navigaciju i turu.");
        builder.SetSmallIcon(Resource.Drawable.ic_stat_tracking);
        builder.SetColor(unchecked((int)0xFF10B981));
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetCategory(NotificationCompat.CategoryNavigation);
        builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);
        builder.SetContentIntent(contentIntent);
        return builder.Build()!;
    }
}
