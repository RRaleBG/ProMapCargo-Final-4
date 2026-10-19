using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.Views;

namespace ProMapCargo.Mobile;

public partial class AppShell : Shell
{
    // The tab shown when the app was last used; after a restart the driver comes back there
    // (navigation with the saved route instead of the home screen).
    private const string LastRouteKey = "shell.last_route";

    // Index of the Poruke tab in AppShell.xaml.
    private const int MessagesTabIndex = 3;

    public AppShell(ChatClient chat)
    {
        InitializeComponent();
        Routing.RegisterRoute(AppNavigation.TripDetails, typeof(TripDetailsPage));
        Routing.RegisterRoute(AppNavigation.Expense, typeof(ExpensePage));
        Routing.RegisterRoute(AppNavigation.Expenses, typeof(ExpensesPage));

        // Unread messages from dispatch as a number on the Poruke tab.
        chat.UnreadChanged += (_, _) => ShowUnread(chat.UnreadCount);
    }

    private static void ShowUnread(int count)
    {
#if ANDROID
        TabBadges.Set(MessagesTabIndex, count);
#endif
    }

    public static string? LastRoute => Preferences.Default.Get<string?>(LastRouteKey, null);

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        var location = args.Current?.Location.OriginalString;
        if (!string.IsNullOrEmpty(location) && !location.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            Preferences.Default.Set(LastRouteKey, location);
        }
    }

    /// <summary>
    /// Android "Nazad": a pushed page goes back, another tab or screen goes to the home
    /// screen, and on the home screen the app only moves to the background, so navigation
    /// and the trip keep their state (the activity is not finished).
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (Navigation.NavigationStack.Count > 1 || Navigation.ModalStack.Count > 0)
        {
            return base.OnBackButtonPressed();
        }

        // A page may close its own panel first (route planner on the navigation screen).
        if (CurrentPage?.SendBackButtonPressed() == true)
        {
            return true;
        }

        var location = CurrentState?.Location.OriginalString ?? string.Empty;
        var onHome = location.EndsWith("dashboard-home", StringComparison.OrdinalIgnoreCase)
                     || location.Contains("login", StringComparison.OrdinalIgnoreCase);
        if (!onHome)
        {
            SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Dashboard));
            return true;
        }

#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
        return true;
#else
        return base.OnBackButtonPressed();
#endif
    }
}
