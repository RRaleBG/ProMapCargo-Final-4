using System.Globalization;
using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Services;

/// <summary>Server answer of api/mobile/app/latest.</summary>
public sealed record AppReleaseInfo(
    [property: JsonPropertyName("versionCode")] int VersionCode,
    [property: JsonPropertyName("versionName")] string? VersionName,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes,
    [property: JsonPropertyName("downloadPath")] string DownloadPath);

/// <summary>
/// Looks on the server for a newer app (at start-up and when the driver comes back to the
/// app, at most every few hours). A newer version shows a phone notification and asks
/// "Ažurirati sada?"; yes downloads the package and opens the Android installer.
/// "Kasnije" asks again the next day.
/// </summary>
public sealed class AppUpdateService(ApiClient apiClient, IMobileNotifier notifier)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan PostponeFor = TimeSpan.FromHours(20);
    private const string PostponedKey = "update.postponed";
    private const int NotificationId = 7001;

    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime lastCheckUtc = DateTime.MinValue;

    public static int InstalledVersionCode
        => int.TryParse(AppInfo.Current.BuildString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : 0;

    /// <summary>
    /// Checks for a newer version. <paramref name="userAsked"/> (Podešavanja → Proveri ažuriranje)
    /// ignores the interval and the "Kasnije" pause and reports "already newest" too.
    /// </summary>
    public async Task CheckAsync(bool userAsked = false)
    {
        if (DeviceInfo.Platform != DevicePlatform.Android)
        {
            return;
        }

        if (!await gate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (!userAsked && DateTime.UtcNow - lastCheckUtc < CheckInterval)
            {
                return;
            }

            lastCheckUtc = DateTime.UtcNow;
            AppReleaseInfo? release;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                release = await apiClient.GetLatestAppAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (userAsked) await notifier.ShowWarningAsync("Server nije dostupan za proveru ažuriranja.").ConfigureAwait(false);
                return;
            }

            if (release is null || release.VersionCode <= InstalledVersionCode)
            {
                if (userAsked) await notifier.ShowInfoAsync($"Aplikacija je ažurna (verzija {AppInfo.Current.VersionString}).").ConfigureAwait(false);
                return;
            }

            if (!userAsked && IsPostponed(release.VersionCode))
            {
                return;
            }

            var title = $"Nova verzija {release.VersionName ?? release.VersionCode.ToString(CultureInfo.InvariantCulture)}";
            LocalNotifications.Show(NotificationId, title, release.Notes is { Length: > 0 } notes ? notes : "Otvorite aplikaciju za ažuriranje.", "promap-updates");

            var accepted = await MainThread.InvokeOnMainThreadAsync(() => Shell.Current is { } shell
                ? shell.DisplayAlertAsync(
                    title,
                    $"{(string.IsNullOrWhiteSpace(release.Notes) ? string.Empty : release.Notes + Environment.NewLine + Environment.NewLine)}" +
                    $"Veličina {release.SizeBytes / 1024d / 1024d:0.0} MB. Ažurirati odmah?",
                    "Ažuriraj",
                    "Kasnije")
                : Task.FromResult(false)).ConfigureAwait(false);

            if (!accepted)
            {
                Preferences.Default.Set(PostponedKey, $"{release.VersionCode}|{DateTime.UtcNow.Add(PostponeFor):O}");
                return;
            }

            await InstallAsync(release).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsPostponed(int versionCode)
    {
        var value = Preferences.Default.Get(PostponedKey, string.Empty);
        var parts = value.Split('|');
        return parts.Length == 2
               && parts[0] == versionCode.ToString(CultureInfo.InvariantCulture)
               && DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until)
               && DateTime.UtcNow < until;
    }

    private async Task InstallAsync(AppReleaseInfo release)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, "promap-cargo-update.apk");
        try
        {
            await notifier.ShowInfoAsync("Preuzimanje nove verzije…").ConfigureAwait(false);
            var lastStep = -1;
            var progress = new Progress<double>(value =>
            {
                var step = (int)(value * 4);
                if (step != lastStep && step is > 0 and < 4)
                {
                    lastStep = step;
                    _ = notifier.ShowInfoAsync($"Preuzimanje {step * 25} %");
                }
            });

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await apiClient.DownloadAppAsync(release.DownloadPath, path, progress, timeout.Token).ConfigureAwait(false);

            // Android shows its installer; the driver confirms there (and the first time allows
            // installing from this app). The app restarts in the new version.
            await MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.OpenAsync(new OpenFileRequest(
                "Ažuriranje ProMap Cargo",
                new ReadOnlyFile(path, "application/vnd.android.package-archive")))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await notifier.ShowErrorAsync($"Ažuriranje nije uspelo: {ex.Message}").ConfigureAwait(false);
        }
    }
}
