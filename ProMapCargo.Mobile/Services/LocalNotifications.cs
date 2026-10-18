namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Phone notifications (document reminders, messages from dispatch). Android only; a tap
/// opens the app. Without the notification permission (Android 13+) nothing is shown.
/// </summary>
public static class LocalNotifications
{
    public static void Show(int id, string title, string text, string channel = "promap-info")
    {
#if ANDROID
        try
        {
            var context = Platform.AppContext;
            if (context.GetSystemService(Android.Content.Context.NotificationService) is not Android.App.NotificationManager manager)
            {
                return;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(26) && manager.GetNotificationChannel(channel) is null)
            {
                var name = channel == "promap-messages" ? "Poruke dispečera" : "Obaveštenja";
                manager.CreateNotificationChannel(new Android.App.NotificationChannel(channel, name, Android.App.NotificationImportance.Default));
            }

            var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
            var flags = OperatingSystem.IsAndroidVersionAtLeast(23)
                ? Android.App.PendingIntentFlags.Immutable | Android.App.PendingIntentFlags.UpdateCurrent
                : Android.App.PendingIntentFlags.UpdateCurrent;
            var pending = open is null ? null : Android.App.PendingIntent.GetActivity(context, id, open, flags);

            var builder = new AndroidX.Core.App.NotificationCompat.Builder(context, channel);
            builder.SetContentTitle(title);
            builder.SetContentText(text);
            builder.SetStyle(new AndroidX.Core.App.NotificationCompat.BigTextStyle().BigText(text));
            builder.SetSmallIcon(Resource.Drawable.ic_stat_tracking);
            builder.SetColor(unchecked((int)0xFF10B981));
            builder.SetAutoCancel(true);
            builder.SetContentIntent(pending);
            manager.Notify(id, builder.Build());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification not shown: {ex.Message}");
        }
#endif
    }
}
