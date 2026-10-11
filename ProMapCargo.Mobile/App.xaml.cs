using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

public partial class App : Application
{
    private readonly AppShell appShell;
    private bool initialNavigationApplied;

    public App(AppShell appShell)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        this.appShell = appShell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(appShell);
        window.Created += HandleWindowCreated;
        return window;
    }

    private async void HandleWindowCreated(object? sender, EventArgs e)
    {
        if (initialNavigationApplied) return;
        initialNavigationApplied = true;

        // Navigation and the map come from the server; offline maps are optional
        // (Početna → Offline mape), so the app always opens on the home screen.
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//dashboard"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Initial navigation failed: {ex}");
        }
    }
}
