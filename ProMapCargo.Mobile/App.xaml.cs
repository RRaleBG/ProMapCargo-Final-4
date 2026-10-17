using AndroidSpecific = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

public partial class App : Application
{
    private readonly AppShell appShell;
    private readonly IServiceProvider services;
    private readonly MobileSessionService session;
    private bool startShown;
    private bool initialNavigationApplied;

    public App(AppShell appShell, IServiceProvider services, MobileSessionService session)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        // The page shrinks above the keyboard, so the route planner (bottom sheet)
        // stays visible while typing instead of being covered.
        AndroidSpecific.Application.UseWindowSoftInputModeAdjust(
            this.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>(),
            AndroidSpecific.WindowSoftInputModeAdjust.Resize);
        this.appShell = appShell;
        this.services = services;
        this.session = session;

        // The server ended the session (refresh token expired or revoked): sign in again.
        session.SessionChanged += (_, _) =>
        {
            if (!session.IsSignedIn && initialNavigationApplied)
            {
                SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Login));
            }
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // A recreated window (e.g. after the system closed the activity) goes straight to the shell.
        if (startShown)
        {
            return new Window(appShell);
        }

        startShown = true;
        // Created here, not injected: its XAML uses the app's resources (Colors.xaml,
        // Styles.xaml), which exist only after InitializeComponent above.
        var startPage = services.GetRequiredService<MainPage>();
        var window = new Window(startPage);
        startPage.Completed += (_, signedIn) => ShowShell(window, signedIn);
        return window;
    }

    // After the start-up screen: the home screen for a signed-in driver, otherwise sign-in
    // (which can be skipped: maps and navigation work without an account).
    private void ShowShell(Window window, bool signedIn)
    {
        if (initialNavigationApplied) return;
        initialNavigationApplied = true;

        window.Page = appShell;
        SafeUi.Run(() => AppNavigation.GoToAsync(signedIn ? AppNavigation.Dashboard : AppNavigation.Login));
    }
}
