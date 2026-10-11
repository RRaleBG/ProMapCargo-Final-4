using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly MobileServerEndpointService serverEndpointService;
    private readonly IMobileNotifier notifier;
    private bool useDarkTheme = true;
    private bool compactNavigationLayout = true;
    private bool enableOfflineRoutingFeatures = true;
    private string accountStatusText = "Prijava je isključena u ovoj verziji aplikacije.";
    private string serverBaseUrl;
    private string serverStatusText;

    public SettingsViewModel(MobileServerEndpointService serverEndpointService, IMobileNotifier notifier)
    {
        this.serverEndpointService = serverEndpointService;
        this.notifier = notifier;
        serverBaseUrl = serverEndpointService.CurrentApiBaseUrl;
        serverStatusText = $"Aktivni server: {serverBaseUrl}";
        OpenMapManagementCommand = new Command(async () => await OpenMapManagementAsync());
        SaveServerBaseUrlCommand = new Command(async () => await SaveServerBaseUrlAsync());
    }

    public ICommand OpenMapManagementCommand { get; }

    public ICommand SaveServerBaseUrlCommand { get; }

    public bool UseDarkTheme
    {
        get => useDarkTheme;
        set => SetProperty(ref useDarkTheme, value);
    }

    public bool CompactNavigationLayout
    {
        get => compactNavigationLayout;
        set => SetProperty(ref compactNavigationLayout, value);
    }

    public bool EnableOfflineRoutingFeatures
    {
        get => enableOfflineRoutingFeatures;
        set => SetProperty(ref enableOfflineRoutingFeatures, value);
    }

    public string AccountStatusText
    {
        get => accountStatusText;
        private set => SetProperty(ref accountStatusText, value);
    }

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
        ServerStatusText = $"Aktivni server: {normalizedApiBaseUrl}";
        await notifier.ShowSuccessAsync("Adresa servera je sačuvana.").ConfigureAwait(false);
    }

    private Task OpenMapManagementAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var shell = Shell.Current;
            if (shell is null)
            {
                return;
            }

            await shell.GoToAsync("//install-maps");
        });
    }
}
