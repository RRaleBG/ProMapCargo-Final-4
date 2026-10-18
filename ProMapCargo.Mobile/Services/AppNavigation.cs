namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Shell navigation that never takes the app down: an unknown route throws
/// ArgumentException inside an async void handler, which closed the app.
/// </summary>
public static class AppNavigation
{
    // Routes defined in AppShell.xaml.
    public const string Dashboard = "//dashboard";
    public const string Navigation = "//dashboard/mobile-navigation";
    public const string Settings = "//settings";
    public const string InstallMaps = "//install-maps";
    public const string Login = "//login";
    public const string TripDetails = "trip-details";
    public const string Expense = "expense";
    public const string Expenses = "expenses";

    public static string GetTripDetailsRoute(Guid tripId)
        => $"{TripDetails}?tripId={tripId:D}";

    public static async Task<bool> GoToAsync(string route)
    {
        try
        {
            var shell = Shell.Current;
            if (shell is null)
            {
                return false;
            }

            await MainThread.InvokeOnMainThreadAsync(() => shell.GoToAsync(route));
            return true;
        }
        catch (Exception ex)
        {
            // Never silent: a button that does nothing looks broken. Tell the driver.
            System.Diagnostics.Debug.WriteLine($"Navigation to '{route}' failed: {ex}");
            try
            {
                if (Shell.Current is { } shell)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        shell.DisplayAlertAsync("Ekran nije otvoren", $"Prelazak na „{route}“ nije uspeo: {ex.Message}", "U redu"));
                }
            }
            catch (Exception)
            {
                // Showing the message must not throw either.
            }

            return false;
        }
    }
}
