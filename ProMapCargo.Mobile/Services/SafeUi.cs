using System.Runtime.CompilerServices;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Async work started from an event handler (OnAppearing, Clicked, view model events).
/// An exception thrown there in an async void method closes the app; here it is logged
/// and shown to the driver instead.
/// </summary>
public static class SafeUi
{
    public static async void Run(Func<Task> action, [CallerMemberName] string caller = "")
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{caller} failed: {ex}");
            ErrorReporter.Current?.Report(ex, caller);
            try
            {
                if (Shell.Current is { } shell)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        shell.DisplayAlertAsync("Greška", $"Radnja nije uspela: {ex.Message}", "U redu"));
                }
            }
            catch (Exception)
            {
                // Showing the message must not throw either.
            }
        }
    }
}
