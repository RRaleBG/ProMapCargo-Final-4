using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class MapInstallViewModel : ViewModelBase
{
    private readonly OfflineMapService offlineMapService;
    private readonly OfflineRoutingBundleService offlineRoutingBundleService;
    private bool isBusy;
    private string statusText = "Loading map packages...";
    private string bundleStatusText = "Offline routing bundles are not installed.";
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
            StatusText = "Fetching map catalog...";

            var catalog = await offlineMapService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var installedManifest = await offlineMapService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
            var bundleCatalog = await offlineRoutingBundleService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var installedBundleManifest = await offlineRoutingBundleService.GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(AvailablePackages, catalog);
                Replace(InstalledPackages, installedManifest.Packages);
                Replace(AvailableBundles, bundleCatalog);
                Replace(InstalledBundles, installedBundleManifest.Bundles);

                OfflineBundleReady = installedBundleManifest.Bundles.Any(bundle =>
                    File.Exists(bundle.GraphLocalPath) && File.Exists(bundle.PmtilesLocalPath));

                BundleStatusText = OfflineBundleReady
                    ? $"Offline routing READY · {installedBundleManifest.Bundles.Count} bundle(s) installed"
                    : "Offline routing NOT READY · install at least one bundle";

                StatusText = catalog.Count == 0
                    ? "No map packages are currently available on server."
                    : $"{catalog.Count} map package(s) available · {bundleCatalog.Count} routing bundle(s) available.";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Map catalog failed: {ex.Message}");
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

        try
        {
            await SetBusyAsync(true);
            StatusText = $"Downloading {package.DisplayName}...";

            var progress = new Progress<double>(value =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    StatusText = $"Downloading {package.DisplayName}: {Math.Round(value * 100)}%";
                });
            });

            await offlineMapService.DownloadPackageAsync(package, progress, CancellationToken.None).ConfigureAwait(false);
            var manifest = await offlineMapService.GetInstalledManifestAsync(CancellationToken.None).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(InstalledPackages, manifest.Packages);
                StatusText = $"Installed {package.DisplayName}.";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Install failed: {ex.Message}");
        }
        finally
        {
            await SetBusyAsync(false);
        }
    }

    private async Task InstallBundleAsync(OfflineRoutingBundleCatalogItem? bundle)
    {
        if (bundle is null || IsBusy)
        {
            return;
        }

        try
        {
            await SetBusyAsync(true);
            StatusText = $"Installing offline bundle {bundle.DisplayName}...";

            var progress = new Progress<double>(value =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    StatusText = $"Installing bundle {bundle.DisplayName}: {Math.Round(value * 100)}%";
                });
            });

            await offlineRoutingBundleService.DownloadAndInstallAsync(bundle, progress, CancellationToken.None).ConfigureAwait(false);
            var installedBundleManifest = await offlineRoutingBundleService.GetInstalledManifestAsync(CancellationToken.None).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(InstalledBundles, installedBundleManifest.Bundles);
                OfflineBundleReady = installedBundleManifest.Bundles.Any(item =>
                    File.Exists(item.GraphLocalPath) && File.Exists(item.PmtilesLocalPath));
                BundleStatusText = OfflineBundleReady
                    ? $"Offline routing READY · {installedBundleManifest.Bundles.Count} bundle(s) installed"
                    : "Offline routing NOT READY · install at least one bundle";
                StatusText = $"Installed offline bundle {bundle.DisplayName}.";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Offline bundle install failed: {ex.Message}");
        }
        finally
        {
            await SetBusyAsync(false);
        }
    }

    private Task SetBusyAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private Task ContinueAsync()
    {
        ContinueRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private Task OpenSettingsAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var shell = Shell.Current;
            if (shell is null)
            {
                return;
            }

            await shell.GoToAsync("//dashboard/settings");
        });
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
