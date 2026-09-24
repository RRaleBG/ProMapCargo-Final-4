using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private bool useDarkTheme = true;
    private bool compactNavigationLayout = true;
    private bool enableOfflineRoutingFeatures = true;
    private string accountStatusText = "Authentication is currently disabled for this build.";

    public SettingsViewModel()
    {
        OpenMapManagementCommand = new Command(async () => await OpenMapManagementAsync());
    }

    public ICommand OpenMapManagementCommand { get; }

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
