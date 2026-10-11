using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class MapInstallViewModel : ViewModelBase
{
    private readonly OfflineMapService offlineMapService;
    private readonly OfflineRoutingBundleService offlineRoutingBundleService;
    private bool isBusy;
    private bool isDownloading;
    private double downloadProgress;
    private string statusText = "Učitavanje paketa mapa…";
    private string bundleStatusText = "Offline paketi za rutiranje nisu instalirani.";
    private bool offlineBundleReady;

    public MapInstallViewModel(OfflineMapService offlineMapService, OfflineRoutingBundleService offlineRoutingBundleService)
    {
        this.offlineMapService = offlineMapService;
        this.offlineRoutingBundleService = offlineRoutingBundleService;

        AvailablePackages = [];
        InstalledPackages = [];
        AvailableBundles = [];
        InstalledBundles = [];

        RefreshCommand = new Command(async () => await LoadAsync(CancellationToken.None));
        InstallCommand = new Command<MapPackageCatalogItem>(async package => await InstallAsync(package));
        InstallBundleCommand = new Command<OfflineRoutingBundleCatalogItem>(async bundle => await InstallBundleAsync(bundle));
        ContinueCommand = new Command(async () => await ContinueAsync());
        OpenSettingsCommand = new Command(async () => await OpenSettingsAsync());
    }

    public event EventHandler? ContinueRequested;

    public ObservableCollection<MapPackageCatalogItem> AvailablePackages { get; }

    public ObservableCollection<InstalledMapPackage> InstalledPackages { get; }

    public ObservableCollection<OfflineRoutingBundleCatalogItem> AvailableBundles { get; }

    public ObservableCollection<InstalledOfflineRoutingBundle> InstalledBundles { get; }

    public ICommand RefreshCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand InstallBundleCommand { get; }

    public ICommand ContinueCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    /// <summary>A package download is running (drives the progress strip in the bottom bar).</summary>
    public bool IsDownloading
    {
        get => isDownloading;
        private set => SetProperty(ref isDownloading, value);
    }

    /// <summary>0..1 progress of the running download.</summary>
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

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            await SetBusyAsync(true);
            StatusText = "Preuzimanje kataloga mapa…";

            var catalog = await offlineMapService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var installedManifest = await offlineMapService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
            var bundleCatalog = await offlineRoutingBundleService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var installedBundleManifest = await offlineRoutingBundleService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(AvailablePackages, catalog.Select(item => item.WithInstallState(installedManifest.Packages)).ToList());
                Replace(InstalledPackages, installedManifest.Packages);
                Replace(AvailableBundles, bundleCatalog.Select(item => item.WithInstallState(installedBundleManifest.Bundles)).ToList());
                Replace(InstalledBundles, installedBundleManifest.Bundles);

                OfflineBundleReady = installedBundleManifest.Bundles.Any(bundle =>
                    File.Exists(bundle.GraphLocalPath) && File.Exists(bundle.PlacesLocalPath));

                BundleStatusText = OfflineBundleReady
                    ? $"Offline rutiranje SPREMNO · instalirano paketa: {installedBundleManifest.Bundles.Count}"
                    : "Offline rutiranje NIJE SPREMNO · instalirajte bar jedan paket";

                StatusText = catalog.Count == 0
                    ? "Trenutno nema dostupnih paketa mapa na serveru."
                    : $"Dostupno paketa mapa: {catalog.Count} · paketa za rutiranje: {bundleCatalog.Count}.";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Katalog mapa nije učitan: {ex.Message}");
        }
        finally
        {
            await SetBusyAsync(false);
        }
    }

    private async Task InstallAsync(MapPackageCatalogItem? package)
    {
        if (package is null || IsBusy)
        {
            return;
        }

        if (package.InstallState == PackageInstallState.Installed)
        {
            StatusText = $"Mapa {package.DisplayName} je već instalirana.";
            return;
        }

        try
        {
            await SetDownloadingAsync(true);
            StatusText = $"Preuzimanje: {package.DisplayName}…";

            var progress = new Progress<double>(value =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DownloadProgress = value;
                    StatusText = $"Preuzimanje mape {package.DisplayName}: {Math.Round(value * 100)}%";
                });
            });

            await offlineMapService.DownloadPackageAsync(package, progress, CancellationToken.None).ConfigureAwait(false);
            var manifest = await offlineMapService.GetInstalledManifestAsync(CancellationToken.None).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(InstalledPackages, manifest.Packages);
                Replace(AvailablePackages, AvailablePackages.Select(item => item.WithInstallState(manifest.Packages)).ToList());
                StatusText = $"Instalirano: {package.DisplayName}.";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Instalacija nije uspela: {ex.Message}");
        }
        finally
        {
            await SetDownloadingAsync(false);
        }
    }

    private async Task InstallBundleAsync(OfflineRoutingBundleCatalogItem? bundle)
    {
        if (bundle is null || IsBusy)
        {
            return;
        }

        if (bundle.InstallState == PackageInstallState.Installed)
        {
            StatusText = $"Paket {bundle.DisplayName} je već instaliran.";
            return;
        }

        try
        {
            await SetDownloadingAsync(true);
            StatusText = $"Instaliranje offline paketa: {bundle.DisplayName}…";

            var progress = new Progress<double>(value =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DownloadProgress = value;
                    StatusText = $"Preuzimanje paketa {bundle.DisplayName}: {Math.Round(value * 100)}%";
                });
            });

            await offlineRoutingBundleService.DownloadAndInstallAsync(bundle, progress, CancellationToken.None).ConfigureAwait(false);
            var installedBundleManifest = await offlineRoutingBundleService.GetInstalledManifestAsync(CancellationToken.None).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(InstalledBundles, installedBundleManifest.Bundles);
                Replace(AvailableBundles, AvailableBundles.Select(item => item.WithInstallState(installedBundleManifest.Bundles)).ToList());
                OfflineBundleReady = installedBundleManifest.Bundles.Any(item =>
                    File.Exists(item.GraphLocalPath) && File.Exists(item.PlacesLocalPath));
                BundleStatusText = OfflineBundleReady
                    ? $"Offline rutiranje SPREMNO · instalirano paketa: {installedBundleManifest.Bundles.Count}"
                    : "Offline rutiranje NIJE SPREMNO · instalirajte bar jedan paket";
                StatusText = $"Instaliran offline paket: {bundle.DisplayName}.";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Instalacija offline paketa nije uspela: {ex.Message}");
        }
        finally
        {
            await SetDownloadingAsync(false);
        }
    }

    private Task SetBusyAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private Task SetDownloadingAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsBusy = value;
            IsDownloading = value;
            DownloadProgress = 0;
        });

    private Task ContinueAsync()
    {
        ContinueRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    // Settings is a top-level Shell route, not a tab under //dashboard (that
    // route did not exist and the unhandled exception closed the app).
    private Task OpenSettingsAsync() => AppNavigation.GoToAsync(AppNavigation.Settings);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
