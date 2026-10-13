using AndroidSpecific = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.Views;

namespace ProMapCargo.Mobile;

public partial class App : Application
{
    private readonly AppShell appShell;
    private readonly StartupPage startupPage;
    private bool startupShown;
    private bool initialNavigationApplied;

    public App(AppShell appShell, StartupPage startupPage)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        // The page shrinks above the keyboard, so the route planner (bottom sheet)
        // stays visible while typing instead of being covered.
        AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            this.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            AndroidSpecific.WindowSoftInputModeAdjust.Resize);
        this.appShell = appShell;
        this.startupPage = startupPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // A recreated window (e.g. after the system closed the activity) goes straight to the shell.
        if (startupShown)
        {
            return new Window(appShell);
        }

        startupShown = true;
        var window = new Window(startupPage);
        startupPage.Completed += (_, _) => ShowShell(window);
        return window;
    }

    // After the start-up screen: navigation and the map come from the server; offline maps
    // are optional (Početna → Offline mape), so the app always opens on the home screen.
    private void ShowShell(Window window)
    {
        if (initialNavigationApplied) return;
        initialNavigationApplied = true;

        window.Page = appShell;
        SafeUi.Run(() => MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync(AppNavigation.Dashboard)));
    }
}
