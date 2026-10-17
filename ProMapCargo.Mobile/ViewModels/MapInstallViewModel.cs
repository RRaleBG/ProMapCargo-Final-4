using System.Collections.ObjectModel;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>An installed map or routing package (the "Na telefonu" list).</summary>
public sealed record InstalledOfflineItem(string Id, bool IsRoutingPackage, string Title, string Detail);

/// <summary>
/// "Offline mape": what is on the phone first (also without signal), then the server's
/// catalog to install or update maps and routing packages, and deleting to free space.
/// </summary>
public sealed class MapInstallViewModel : ViewModelBase
{
    private readonly OfflineMapService offlineMapService;
    private readonly OfflineRoutingBundleService offlineRoutingBundleService;
    private readonly OfflineRoutingEngine offlineEngine;
    private readonly OfflineWebCache offlineWebCache;
    private readonly IMobileNotifier notifier;
    private string statusText = "Učitavanje…";
    private string storageText = string.Empty;
    private string bundleStatusText = string.Empty;
    private bool isBusy;
    private bool isDownloading;
    private bool isOffline;
    private bool offlineBundleReady;
    private bool offlineMapReady;
    private double downloadProgress;
    private CancellationTokenSource? downloadCts;

    public MapInstallViewModel(
        OfflineMapService offlineMapService,
        OfflineRoutingBundleService offlineRoutingBundleService,
        OfflineRoutingEngine offlineEngine,
        OfflineWebCache offlineWebCache,
        IMobileNotifier notifier)
    {
        this.offlineMapService = offlineMapService;
        this.offlineRoutingBundleService = offlineRoutingBundleService;
        this.offlineEngine = offlineEngine;
        this.offlineWebCache = offlineWebCache;
        this.notifier = notifier;

        RefreshCommand = new Command(async () =>
        {
            await LoadAsync(CancellationToken.None);
            IsPullRefreshing = false;
        });
        InstallCommand = new Command<MapPackageCatalogItem>(async package => await InstallAsync(package));
        InstallBundleCommand = new Command<OfflineRoutingBundleCatalogItem>(async bundle => await InstallBundleAsync(bundle));
        CancelDownloadCommand = new Command(() => downloadCts?.Cancel());
        BackCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Dashboard));
    }

    public ObservableCollection<MapPackageCatalogItem> AvailablePackages { get; } = new();

    public ObservableCollection<OfflineRoutingBundleCatalogItem> AvailableBundles { get; } = new();

    public ObservableCollection<InstalledOfflineItem> InstalledItems { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand InstallBundleCommand { get; }

    public ICommand CancelDownloadCommand { get; }

    public ICommand BackCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string StorageText
    {
        get => storageText;
        private set => SetProperty(ref storageText, value);
    }

    // Pull-to-refresh only (see DashboardViewModel.IsPullRefreshing).
    private bool isPullRefreshing;

    public bool IsPullRefreshing
    {
        get => isPullRefreshing;
        set => SetProperty(ref isPullRefreshing, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public bool IsDownloading
    {
        get => isDownloading;
        private set => SetProperty(ref isDownloading, value);
    }

    public bool IsOffline
    {
        get => isOffline;
        private set => SetProperty(ref isOffline, value);
    }

    public double DownloadProgress
    {
        get => downloadProgress;
        private set => SetProperty(ref downloadProgress, value);
    }

    public string BundleStatusText
    {
        get => bundleStatusText;
        private set => SetProperty(ref bundleStatusText, value);
    }

    /// <summary>Routing works without signal (a routing package is installed).</summary>
    public bool OfflineBundleReady
    {
        get => offlineBundleReady;
        private set => SetProperty(ref offlineBundleReady, value);
    }

    /// <summary>The map shows without signal (a navigation map is installed).</summary>
    public bool OfflineMapReady
    {
        get => offlineMapReady;
        private set => SetProperty(ref offlineMapReady, value);
    }

    public bool OfflineReady => OfflineBundleReady && OfflineMapReady;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            await SetBusyAsync(true);

            // The phone first: shown at once and without signal.
            var installedMaps = await offlineMapService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
            var installedBundles = await offlineRoutingBundleService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => ApplyInstalled(installedMaps, installedBundles));

            await MainThread.InvokeOnMainThreadAsync(() => StatusText = "Učitavanje kataloga sa servera…");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                var catalog = await offlineMapService.GetCatalogAsync(timeout.Token).ConfigureAwait(false);
                var bundleCatalog = await offlineRoutingBundleService.GetCatalogAsync(timeout.Token).ConfigureAwait(false);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsOffline = false;
                    Replace(AvailablePackages, catalog.Select(item => item.WithInstallState(installedMaps.Packages)).ToList());
                    Replace(AvailableBundles, bundleCatalog.Select(item => item.WithInstallState(installedBundles.Bundles)).ToList());
                    var updates = AvailablePackages.Count(item => item.InstallState == PackageInstallState.Update) +
                                  AvailableBundles.Count(item => item.InstallState == PackageInstallState.Update);
                    StatusText = updates > 0
                        ? $"Dostupno ažuriranja: {updates}"
                        : catalog.Count + bundleCatalog.Count == 0
                            ? "Na serveru trenutno nema paketa."
                            : "Sve instalirano je ažurno.";
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
            {
                offlineWebCache.MarkOffline();
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsOffline = true;
                    StatusText = "Bez veze sa serverom. Instalirane mape i paketi rade i bez interneta.";
                });
            }
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Mape nisu učitane: {ex.Message}");
        }
        finally
        {
            await SetBusyAsync(false);
        }
    }

    private void ApplyInstalled(InstalledMapManifest maps, InstalledOfflineRoutingManifest bundles)
    {
        var items = new List<InstalledOfflineItem>();
        long total = 0;

        foreach (var map in maps.Packages.Where(map => File.Exists(map.LocalPath)))
        {
            var size = FileSize(map.LocalPath);
            total += size;
            items.Add(new InstalledOfflineItem(map.Id, false, map.DisplayName, $"Mapa · {ByteSize.Format(size)} · {map.InstalledAtUtc.ToLocalTime():dd.MM.yyyy.}"));
        }

        foreach (var bundle in bundles.Bundles.Where(bundle => File.Exists(bundle.GraphLocalPath)))
        {
            var size = FileSize(bundle.GraphLocalPath) + FileSize(bundle.PlacesLocalPath);
            total += size;
            items.Add(new InstalledOfflineItem(bundle.Id, true, bundle.DisplayName, $"Rutiranje · {ByteSize.Format(size)} · verzija {bundle.Version}"));
        }

        Replace(InstalledItems, items);
        // Buttons in the catalog follow the phone right away (the catalog reload takes a moment).
        Replace(AvailablePackages, AvailablePackages.Select(item => item.WithInstallState(maps.Packages)).ToList());
        Replace(AvailableBundles, AvailableBundles.Select(item => item.WithInstallState(bundles.Bundles)).ToList());

        OfflineMapReady = maps.Packages.Any(map => File.Exists(map.LocalPath));
        OfflineBundleReady = bundles.Bundles.Any(bundle => File.Exists(bundle.GraphLocalPath) && File.Exists(bundle.PlacesLocalPath));
        RaisePropertyChanged(nameof(OfflineReady));
        BundleStatusText = (OfflineMapReady, OfflineBundleReady) switch
        {
            (true, true) => "Spremno za rad bez interneta: mapa i rute na telefonu.",
            (true, false) => "Mapa radi bez interneta. Za rute bez signala instalirajte paket za rutiranje.",
            (false, true) => "Rute rade bez interneta. Za prikaz mape bez signala instalirajte mapu.",
            _ => "Za rad bez interneta instalirajte mapu i paket za rutiranje svog regiona.",
        };

        var free = FreeSpace();
        var used = total > 0 ? $"Na telefonu: {ByteSize.Format(total)}" : "Na telefonu nema mapa";
        StorageText = free > 0 ? $"{used} · slobodno {ByteSize.Format(free)}" : used;
    }

    private async Task InstallAsync(MapPackageCatalogItem? package)
    {
        if (package is null || await IsBusyNotifiedAsync())
        {
            return;
        }

        if (package.InstallState == PackageInstallState.Installed)
        {
            return;
        }

        if (!await HasSpaceForAsync(package.SizeBytes))
        {
            return;
        }

        await RunDownloadAsync(package.DisplayName, async (progress, ct) =>
        {
            await offlineMapService.DownloadPackageAsync(package, progress, ct).ConfigureAwait(false);
            offlineWebCache.InvalidateLocalMap();
        });
    }

    private async Task InstallBundleAsync(OfflineRoutingBundleCatalogItem? bundle)
    {
        if (bundle is null || await IsBusyNotifiedAsync())
        {
            return;
        }

        if (bundle.InstallState == PackageInstallState.Installed)
        {
            return;
        }

        if (!await HasSpaceForAsync(bundle.GraphSizeBytes + bundle.PlacesSizeBytes))
        {
            return;
        }

        await RunDownloadAsync(bundle.DisplayName, async (progress, ct) =>
        {
            await offlineRoutingBundleService.DownloadAndInstallAsync(bundle, progress, ct).ConfigureAwait(false);
            await offlineEngine.UnloadAsync().ConfigureAwait(false);
        });
    }

    // One download at a time; it can be cancelled and resumes where it stopped.
    private async Task RunDownloadAsync(string name, Func<IProgress<double>, CancellationToken, Task> download)
    {
        downloadCts = new CancellationTokenSource();
        try
        {
            await SetDownloadingAsync(true);
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Preuzimanje: {name}…");

            var progress = new Progress<double>(value => MainThread.BeginInvokeOnMainThread(() =>
            {
                DownloadProgress = value;
                // At 100% the file is checked against the server's SHA-256 (a few seconds).
                StatusText = value >= 1
                    ? $"Provera: {name}…"
                    : $"Preuzimanje: {name} · {Math.Round(value * 100)}%";
            }));

            await download(progress, downloadCts.Token).ConfigureAwait(false);
            await notifier.ShowSuccessAsync($"Instalirano: {name}.").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (downloadCts.IsCancellationRequested)
        {
            await notifier.ShowInfoAsync("Preuzimanje je prekinuto. Nastaviće se odakle je stalo.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await notifier.ShowErrorAsync($"Instalacija nije uspela: {ApiClient.DescribeError(ex)}").ConfigureAwait(false);
        }
        finally
        {
            downloadCts.Dispose();
            downloadCts = null;
            await SetDownloadingAsync(false);
        }

        await LoadAsync(CancellationToken.None);
    }

    /// <summary>Deletes an installed map or routing package (the page confirms first).</summary>
    public async Task DeleteAsync(InstalledOfflineItem item)
    {
        if (await IsBusyNotifiedAsync())
        {
            return;
        }

        try
        {
            if (item.IsRoutingPackage)
            {
                await offlineEngine.UnloadAsync().ConfigureAwait(false);
                await offlineRoutingBundleService.DeleteAsync(item.Id, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await offlineMapService.DeleteAsync(item.Id, CancellationToken.None).ConfigureAwait(false);
                offlineWebCache.InvalidateLocalMap();
            }

            await notifier.ShowSuccessAsync($"Obrisano: {item.Title}.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await notifier.ShowErrorAsync($"Brisanje nije uspelo: {ex.Message}").ConfigureAwait(false);
        }

        await LoadAsync(CancellationToken.None);
    }

    private async Task<bool> HasSpaceForAsync(long bytes)
    {
        var free = FreeSpace();
        // Room for the download plus a margin for the system.
        if (free <= 0 || free > bytes + (200L << 20))
        {
            return true;
        }

        await notifier.ShowWarningAsync($"Nema dovoljno mesta: potrebno {ByteSize.Format(bytes)}, slobodno {ByteSize.Format(free)}.").ConfigureAwait(false);
        return false;
    }

    // One download at a time: a tap meanwhile says why nothing starts instead of doing nothing.
    private async Task<bool> IsBusyNotifiedAsync()
    {
        if (!IsDownloading)
        {
            return false;
        }

        await notifier.ShowInfoAsync("Sačekajte da se završi preuzimanje koje je u toku.");
        return true;
    }

    private static long FileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static long FreeSpace()
    {
        try
        {
#if ANDROID
            // The data partition of the app (DriveInfo would report the root file system).
            return new Android.OS.StatFs(FileSystem.AppDataDirectory).AvailableBytes;
#else
            return new DriveInfo(Path.GetPathRoot(FileSystem.AppDataDirectory) ?? FileSystem.AppDataDirectory).AvailableFreeSpace;
#endif
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private Task SetBusyAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private Task SetDownloadingAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsDownloading = value;
            DownloadProgress = 0;
        });

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
