using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class MapInstallViewModel : ViewModelBase
{
    private readonly OfflineMapService offlineMapService;
    private bool isBusy;
    private string statusText = "Loading map packages...";

    public MapInstallViewModel(OfflineMapService offlineMapService)
    {
        this.offlineMapService = offlineMapService;

        AvailablePackages = [];
        InstalledPackages = [];

        RefreshCommand = new Command(async () => await LoadAsync(CancellationToken.None));
        InstallCommand = new Command<MapPackageCatalogItem>(async package => await InstallAsync(package));
        ContinueCommand = new Command(async () => await ContinueAsync());
    }

    public event EventHandler? ContinueRequested;

    public ObservableCollection<MapPackageCatalogItem> AvailablePackages { get; }

    public ObservableCollection<InstalledMapPackage> InstalledPackages { get; }

    public ICommand RefreshCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand ContinueCommand { get; }

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

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(AvailablePackages, catalog);
                Replace(InstalledPackages, installedManifest.Packages);
                StatusText = catalog.Count == 0
                    ? "No map packages are currently available on server."
                    : $"{catalog.Count} package(s) available.";
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

    private Task SetBusyAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private Task ContinueAsync()
    {
        ContinueRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
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
