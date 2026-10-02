using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

public partial class App : Application
{
    private readonly OfflineMapService offlineMapService;
    private readonly AppShell appShell;
    private bool initialNavigationApplied;

    public App(OfflineMapService offlineMapService, AppShell appShell)
    {
        InitializeComponent();
        this.offlineMapService = offlineMapService;
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

        try
        {
            // Stvaramo CancellationToken koji prekida provere posle 3 sekunde ako backend ne odgovara
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            var hasInstalledMaps = await offlineMapService.HasInstalledMapsAsync(cts.Token).ConfigureAwait(false);
            if (!hasInstalledMaps)
            {
                await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//install-maps"));
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//dashboard"));
        }
        catch
        {
            // Ako backend padne ili izbaci timeout, bezbedno preusmeri na install-maps
            await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//install-maps"));
        }
    }
}
