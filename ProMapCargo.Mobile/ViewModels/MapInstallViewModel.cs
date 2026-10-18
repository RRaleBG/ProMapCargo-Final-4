using System.Collections.ObjectModel;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>
/// One offline region as the driver sees it: the map (tiles) and the routing package
/// together, installed, updated and deleted as one, so a region never has a map without
/// routes or routes without a map.
/// </summary>
public sealed record RegionPackage(
    string Key,
    string Title,
    string Detail,
    string ActionText,
    bool IsInstalled,
    bool CanInstall,
    MapPackageCatalogItem? Map,
    OfflineRoutingBundleCatalogItem? Bundle);

/// <summary>A region on the phone (map and/or routing).</summary>
public sealed record InstalledRegion(string Key, string Title, string Detail, string? MapId, string? BundleId);

/// <summary>
/// "Offline mape": regions on the phone first (also without signal), then the server's
/// regions to install or update (map + routing in one step), and deleting to free space.
/// </summary>
public sealed class MapInstallViewModel : ViewModelBase
{
    private const string MapSuffix = "-nav";
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
    private bool isPullRefreshing;
    private double downloadProgress;
    private CancellationTokenSource? downloadCts;
    private IReadOnlyList<MapPackageCatalogItem> mapCatalog = [];
    private IReadOnlyList<OfflineRoutingBundleCatalogItem> bundleCatalog = [];
    private InstalledMapManifest installedMaps = InstalledMapManifest.Empty;
    private InstalledOfflineRoutingManifest installedBundles = InstalledOfflineRoutingManifest.Empty;

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
        CancelDownloadCommand = new Command(() => downloadCts?.Cancel());
        BackCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Dashboard));
    }

    public ObservableCollection<RegionPackage> Regions { get; } = new();

    public ObservableCollection<InstalledRegion> InstalledItems { get; } = new();

    public ICommand RefreshCommand { get; }

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

    public bool OfflineBundleReady
    {
        get => offlineBundleReady;
        private set => SetProperty(ref offlineBundleReady, value);
    }

    public bool OfflineMapReady
    {
        get => offlineMapReady;
        private set => SetProperty(ref offlineMapReady, value);
    }

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
            installedMaps = await offlineMapService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
            installedBundles = await offlineRoutingBundleService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(ApplyState);

            await MainThread.InvokeOnMainThreadAsync(() => StatusText = "Učitavanje regiona sa servera…");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                mapCatalog = await offlineMapService.GetCatalogAsync(timeout.Token).ConfigureAwait(false);
                bundleCatalog = await offlineRoutingBundleService.GetCatalogAsync(timeout.Token).ConfigureAwait(false);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsOffline = false;
                    ApplyState();
                    var updates = Regions.Count(region => region.ActionText == PackageInstallState.Update);
                    StatusText = updates > 0
                        ? $"Dostupno ažuriranja: {updates}"
                        : Regions.Count == 0 ? "Na serveru trenutno nema regiona." : "Sve instalirano je ažurno.";
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
            {
                offlineWebCache.MarkOffline();
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsOffline = true;
                    StatusText = "Bez veze sa serverom. Instalirani regioni rade i bez interneta.";
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

    // Region key: the routing package id; maps carry the same name with "-nav".
    private static string KeyOf(string mapId) => mapId.EndsWith(MapSuffix, StringComparison.OrdinalIgnoreCase) ? mapId[..^MapSuffix.Length] : mapId;

    private void ApplyState()
    {
        // ---- On the phone, by region ----
        var mapsOnPhone = installedMaps.Packages.Where(map => File.Exists(map.LocalPath)).ToList();
        var bundlesOnPhone = installedBundles.Bundles.Where(bundle => File.Exists(bundle.GraphLocalPath)).ToList();
        long total = 0;
        var installed = new List<InstalledRegion>();
        foreach (var key in mapsOnPhone.Select(map => KeyOf(map.Id)).Concat(bundlesOnPhone.Select(bundle => bundle.Id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var map = mapsOnPhone.FirstOrDefault(item => string.Equals(KeyOf(item.Id), key, StringComparison.OrdinalIgnoreCase));
            var bundle = bundlesOnPhone.FirstOrDefault(item => string.Equals(item.Id, key, StringComparison.OrdinalIgnoreCase));
            var size = (map is null ? 0 : FileSize(map.LocalPath)) +
                       (bundle is null ? 0 : FileSize(bundle.GraphLocalPath) + FileSize(bundle.PlacesLocalPath));
            total += size;
            var parts = (map, bundle) switch
            {
                ({ }, { }) => "mapa i rute",
                ({ }, null) => "samo mapa · rute nisu preuzete",
                _ => "samo rute · mapa nije preuzeta",
            };
            installed.Add(new InstalledRegion(key, bundle?.DisplayName ?? map!.DisplayName, $"{parts} · {ByteSize.Format(size)}", map?.Id, bundle?.Id));
        }

        Replace(InstalledItems, installed.OrderBy(item => item.Title).ToList());

        // ---- Regions on the server: map + routing as one package ----
        var regions = new List<RegionPackage>();
        foreach (var key in mapCatalog.Select(map => KeyOf(map.Id)).Concat(bundleCatalog.Select(bundle => bundle.Id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var map = mapCatalog.FirstOrDefault(item => string.Equals(KeyOf(item.Id), key, StringComparison.OrdinalIgnoreCase))?.WithInstallState(installedMaps.Packages);
            var bundle = bundleCatalog.FirstOrDefault(item => string.Equals(item.Id, key, StringComparison.OrdinalIgnoreCase))?.WithInstallState(installedBundles.Bundles);
            var states = new[] { map?.InstallState, bundle?.InstallState }.Where(state => state is not null).ToList();
            var action = states.All(state => state == PackageInstallState.Installed)
                ? PackageInstallState.Installed
                : states.Any(state => state == PackageInstallState.Update) || states.Contains(PackageInstallState.Installed)
                    ? PackageInstallState.Update
                    : PackageInstallState.Install;
            var size = (map?.SizeBytes ?? 0) + (bundle is null ? 0 : bundle.GraphSizeBytes + bundle.PlacesSizeBytes);
            var detail = bundle is null
                ? $"{ByteSize.Format(size)} · samo mapa, bez rutiranja"
                : $"{ByteSize.Format(size)} · mapa i rute za kamion";
            regions.Add(new RegionPackage(key, bundle?.DisplayName ?? map!.DisplayName, detail, action, action == PackageInstallState.Installed, action != PackageInstallState.Installed, map, bundle));
        }

        Replace(Regions, regions.OrderBy(region => region.Bundle is null).ThenBy(region => region.Title).ToList());

        OfflineMapReady = mapsOnPhone.Count > 0;
        OfflineBundleReady = bundlesOnPhone.Any(bundle => File.Exists(bundle.PlacesLocalPath));
        BundleStatusText = (OfflineMapReady, OfflineBundleReady) switch
        {
            (true, true) => "Spremno za rad bez interneta: mapa i rute na telefonu.",
            (true, false) or (false, true) => "Region nije potpun: dopunite ga ispod (mapa i rute idu zajedno).",
            _ => "Za rad bez interneta instalirajte region kojim vozite.",
        };

        var free = FreeSpace();
        var used = total > 0 ? $"Na telefonu: {ByteSize.Format(total)}" : "Na telefonu nema regiona";
        StorageText = free > 0 ? $"{used} · slobodno {ByteSize.Format(free)}" : used;
    }

    /// <summary>Installs or updates the region: whichever part is missing or older, map first.</summary>
    public async Task InstallAsync(RegionPackage region)
    {
        if (await IsBusyNotifiedAsync() || !region.CanInstall)
        {
            return;
        }

        var map = region.Map is { InstallState: not PackageInstallState.Installed } m ? m : null;
        var bundle = region.Bundle is { InstallState: not PackageInstallState.Installed } b ? b : null;
        long mapBytes = map?.SizeBytes ?? 0;
        long bundleBytes = bundle is null ? 0 : bundle.GraphSizeBytes + bundle.PlacesSizeBytes;
        if (!await HasSpaceForAsync(mapBytes + bundleBytes))
        {
            return;
        }

        var totalBytes = (double)Math.Max(mapBytes + bundleBytes, 1);
        await RunDownloadAsync(region.Title, async (progress, ct) =>
        {
            if (map is not null)
            {
                await offlineMapService.DownloadPackageAsync(map, new Progress<double>(v => progress.Report(v * mapBytes / totalBytes)), ct).ConfigureAwait(false);
                offlineWebCache.InvalidateLocalMap();
            }

            if (bundle is not null)
            {
                await offlineRoutingBundleService.DownloadAndInstallAsync(bundle, new Progress<double>(v => progress.Report((mapBytes + v * bundleBytes) / totalBytes)), ct).ConfigureAwait(false);
                await offlineEngine.UnloadAsync().ConfigureAwait(false);
            }
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
                StatusText = value >= 0.999
                    ? $"Provera: {name}…"
                    : $"Preuzimanje: {name} · {Math.Round(value * 100)}%";
            }));

            await download(progress, downloadCts.Token).ConfigureAwait(false);
            await notifier.ShowSuccessAsync($"Region „{name}“ je spreman za rad bez interneta.").ConfigureAwait(false);
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

    /// <summary>Deletes the region from the phone: map and routing together (the page confirms).</summary>
    public async Task DeleteAsync(InstalledRegion region)
    {
        if (await IsBusyNotifiedAsync())
        {
            return;
        }

        try
        {
            if (region.BundleId is { } bundleId)
            {
                await offlineEngine.UnloadAsync().ConfigureAwait(false);
                await offlineRoutingBundleService.DeleteAsync(bundleId, CancellationToken.None).ConfigureAwait(false);
            }

            if (region.MapId is { } mapId)
            {
                await offlineMapService.DeleteAsync(mapId, CancellationToken.None).ConfigureAwait(false);
                offlineWebCache.InvalidateLocalMap();
            }

            await notifier.ShowSuccessAsync($"Obrisano: {region.Title}.").ConfigureAwait(false);
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
