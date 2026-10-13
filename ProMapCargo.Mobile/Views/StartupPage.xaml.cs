using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.Views;

/// <summary>
/// Start-up screen after the system splash. The bar follows real start-up steps (saved
/// sign-in, installed offline maps and routing packages, all read locally, nothing is
/// changed), animated smoothly to 100 %; then Completed opens the home screen.
/// </summary>
public partial class StartupPage : ContentPage
{
    private const string ProgressAnimation = "StartupProgress";
    private const string PulseAnimation = "StartupPulse";
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(5);

    private readonly TokenStore tokenStore;
    private readonly OfflineMapService offlineMaps;
    private readonly OfflineRoutingBundleService offlineRouting;
    private bool started;
    private double shown;

    public event EventHandler? Completed;

    public StartupPage(TokenStore tokenStore, OfflineMapService offlineMaps, OfflineRoutingBundleService offlineRouting)
    {
        InitializeComponent();
        this.tokenStore = tokenStore;
        this.offlineMaps = offlineMaps;
        this.offlineRouting = offlineRouting;
        VersionLabel.Text = $"Verzija {AppInfo.Current.VersionString}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartPulse();
        if (started) return;
        started = true;
        SafeUi.Run(RunAsync);
    }

    protected override void OnDisappearing()
    {
        this.AbortAnimation(PulseAnimation);
        base.OnDisappearing();
    }

    private async Task RunAsync()
    {
        await StepAsync("Pokretanje aplikacije…", 20, _ => Task.CompletedTask);
        await StepAsync("Provera prijave…", 45, ct => tokenStore.GetAsync(ct));
        await StepAsync("Offline mape…", 70, ct => offlineMaps.GetInstalledManifestAsync(ct));
        await StepAsync("Offline rute…", 90, ct => offlineRouting.GetInstalledManifestAsync(ct));
        await StepAsync("Spremno", 100, _ => Task.CompletedTask);

        await Task.Delay(180);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    // The bar moves while the step runs; a slow or failing step never blocks the start.
    private async Task StepAsync(string text, double target, Func<CancellationToken, Task> work)
    {
        StatusLabel.Text = text;
        var animation = AnimateProgressAsync(target, 320);

        using var timeout = new CancellationTokenSource(StepTimeout);
        try
        {
            await work(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Start-up step '{text}' skipped: {ex.Message}");
        }

        await animation;
    }

    private Task AnimateProgressAsync(double target, uint durationMs)
    {
        var done = new TaskCompletionSource();
        this.AbortAnimation(ProgressAnimation);
        new Animation(SetProgress, shown, target, Easing.CubicOut)
            .Commit(this, ProgressAnimation, 16, durationMs, finished: (_, _) => done.TrySetResult());
        return done.Task;
    }

    private void SetProgress(double value)
    {
        shown = value;
        ProgressFill.ScaleX = value / 100;
        PercentLabel.Text = $"{Math.Round(value)}%";
    }

    // The diamond breathes like the web sidebar mark (scale 1 ↔ 1.14), the halo with it.
    private void StartPulse()
    {
        this.AbortAnimation(PulseAnimation);
        new Animation
        {
            { 0, 0.5, new Animation(v => { Diamond.Scale = v; Halo.Opacity = 0.35 + (v - 1) * 2.5; }, 1, 1.14, Easing.SinInOut) },
            { 0.5, 1, new Animation(v => { Diamond.Scale = v; Halo.Opacity = 0.35 + (v - 1) * 2.5; }, 1.14, 1, Easing.SinInOut) },
        }.Commit(this, PulseAnimation, 16, 1600, repeat: () => true);
    }
}
