using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>
/// Podešavanja: account (sign in / out), position for dispatch, navigation preferences,
/// server address with a connection test, offline data, version. Every switch is stored
/// on the phone and used right away.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly MobileServerEndpointService serverEndpointService;
    private readonly MobileSessionService sessionService;
    private readonly GpsTelemetryUploader telemetry;
    private readonly ApiClient apiClient;
    private readonly OfflineWebCache offlineWebCache;
    private readonly DriverPortalStore portalStore;
    private readonly IMobileNotifier notifier;
    private string serverBaseUrl;
    private string serverStatusText;
    private bool isTestingServer;

    public SettingsViewModel(
        MobileServerEndpointService serverEndpointService,
        MobileSessionService sessionService,
        GpsTelemetryUploader telemetry,
        ApiClient apiClient,
        OfflineWebCache offlineWebCache,
        DriverPortalStore portalStore,
        IMobileNotifier notifier)
    {
        this.serverEndpointService = serverEndpointService;
        this.sessionService = sessionService;
        this.telemetry = telemetry;
        this.apiClient = apiClient;
        this.offlineWebCache = offlineWebCache;
        this.portalStore = portalStore;
        this.notifier = notifier;
        serverBaseUrl = serverEndpointService.CurrentApiBaseUrl;
        serverStatusText = "Telefon i računar sa serverom moraju biti na istoj mreži.";

        OpenMapManagementCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.InstallMaps));
        SignInCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Login));
        SaveServerBaseUrlCommand = new Command(async () => await SaveServerBaseUrlAsync());
        TestServerCommand = new Command(async () => await TestServerAsync(), () => !isTestingServer);
        ClearCacheCommand = new Command(ClearCache);
        BackCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Dashboard));

        telemetry.StatusChanged += (_, _) => RaisePropertyChanged(nameof(LocationStatusText));
    }

    public ICommand OpenMapManagementCommand { get; }

    public ICommand SignInCommand { get; }

    public ICommand SaveServerBaseUrlCommand { get; }

    public ICommand TestServerCommand { get; }

    public ICommand ClearCacheCommand { get; }

    public ICommand BackCommand { get; }

    // ---- Account ----

    public bool IsSignedIn => sessionService.IsSignedIn;

    public bool IsSignedOut => !sessionService.IsSignedIn;

    public string AccountName => sessionService.CurrentSession?.User.DisplayName ?? "Niste prijavljeni";

    public string AccountDetail => sessionService.CurrentSession?.User is { } user
        ? $"{user.Email} · {(sessionService.IsDriver ? "vozač" : string.Join(", ", user.Roles))}"
        : "Prijavite se nalogom vozača da vidite turu i šaljete lokaciju dispečeru.";

    public string AccountInitials => sessionService.CurrentSession?.User.DisplayName is { Length: > 0 } name
        ? string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])))
        : "?";

    public async Task LogoutAsync()
    {
        await telemetry.StopAsync(clearQueue: true).ConfigureAwait(false);
        portalStore.Clear();
        await sessionService.LogoutAsync().ConfigureAwait(false);
        await notifier.ShowInfoAsync("Odjavljeni ste.").ConfigureAwait(false);
    }

    // ---- Position and navigation ----

    public bool ShareLocation
    {
        get => AppSettings.ShareLocation;
        set
        {
            if (AppSettings.ShareLocation == value) return;
            AppSettings.ShareLocation = value;
            RaisePropertyChanged();
            // Off: stops at once. On: the home screen starts it with the running trip on its
            // next load (it knows whether the trip is running).
            if (!value)
            {
                SafeUi.Run(() => telemetry.StopAsync(clearQueue: false));
            }

            RaisePropertyChanged(nameof(LocationStatusText));
        }
    }

    public string LocationStatusText => telemetry.StatusText;

    public bool VoiceGuidance
    {
        get => AppSettings.VoiceGuidance;
        set
        {
            if (AppSettings.VoiceGuidance == value) return;
            AppSettings.VoiceGuidance = value;
            RaisePropertyChanged();
        }
    }

    public bool KeepScreenOn
    {
        get => AppSettings.KeepScreenOn;
        set
        {
            if (AppSettings.KeepScreenOn == value) return;
            AppSettings.KeepScreenOn = value;
            RaisePropertyChanged();
        }
    }

    // ---- Server ----

    public string ServerBaseUrl
    {
        get => serverBaseUrl;
        set => SetProperty(ref serverBaseUrl, value);
    }

    public string ServerStatusText
    {
        get => serverStatusText;
        private set => SetProperty(ref serverStatusText, value);
    }

    private async Task SaveServerBaseUrlAsync()
    {
        if (!serverEndpointService.TrySaveApiBaseUrl(ServerBaseUrl, out var normalizedApiBaseUrl, out var errorMessage))
        {
            ServerStatusText = errorMessage;
            await notifier.ShowWarningAsync(errorMessage).ConfigureAwait(false);
            return;
        }

        ServerBaseUrl = normalizedApiBaseUrl;
        await notifier.ShowSuccessAsync("Adresa servera je sačuvana.").ConfigureAwait(false);
        await TestServerAsync().ConfigureAwait(false);
    }

    private async Task TestServerAsync()
    {
        isTestingServer = true;
        (TestServerCommand as Command)?.ChangeCanExecute();
        ServerStatusText = "Provera veze…";
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var reachable = await apiClient.IsServerReachableAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            ServerStatusText = reachable
                ? $"Server odgovara ({watch.ElapsedMilliseconds} ms)."
                : $"Server {serverEndpointService.CurrentApiBaseUrl} ne odgovara. Proverite adresu i Wi‑Fi.";
        }
        finally
        {
            isTestingServer = false;
            await MainThread.InvokeOnMainThreadAsync(() => (TestServerCommand as Command)?.ChangeCanExecute());
        }
    }

    // ---- Offline data ----

    public string CacheText => $"Sačuvana navigacija i slova mape: {ByteSize.Format(offlineWebCache.CacheSizeBytes())}";

    private void ClearCache()
    {
        offlineWebCache.ClearCache();
        RaisePropertyChanged(nameof(CacheText));
        SafeUi.Run(() => notifier.ShowInfoAsync("Kopija je obrisana. Ponovo se čuva čim server odgovori."));
    }

    // ---- About ----

    public string VersionText => $"ProMap Cargo {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString}) · {DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}, Android {DeviceInfo.Current.VersionString}";

    /// <summary>The page appears: account and sizes may have changed elsewhere.</summary>
    public void Refresh()
    {
        ServerBaseUrl = serverEndpointService.CurrentApiBaseUrl;
        foreach (var name in new[]
                 {
                     nameof(IsSignedIn), nameof(IsSignedOut), nameof(AccountName), nameof(AccountDetail), nameof(AccountInitials),
                     nameof(ShareLocation), nameof(LocationStatusText), nameof(VoiceGuidance), nameof(KeepScreenOn), nameof(CacheText),
                 })
        {
            RaisePropertyChanged(name);
        }
    }
}
