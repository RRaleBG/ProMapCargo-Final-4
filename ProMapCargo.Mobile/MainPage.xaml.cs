using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

/// <summary>
/// Start-up screen. The bar and the truck follow the real start-up steps (saved sign-in,
/// installed offline maps and routing packages, the server), all of which only read;
/// a slow or failing step is skipped after a few seconds and never blocks the start.
/// Then <see cref="Completed"/> opens sign-in or the home screen.
/// </summary>
public partial class MainPage : ContentPage
{
    private const string ProgressAnimation = "StartupProgress";
    private const string SceneAnimation = "StartupScene";
    private const double RouteKilometres = 94;
    private const double TruckHalfSize = 23; // the truck tile is 46 × 46 (MainPage.xaml)
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(5);

    // The web route (Pages/Index.cshtml, viewBox 610 × 480) at half size: four cubic segments.
    private static readonly (double X, double Y)[][] RouteSegments =
    [
        [(36, 183), (72.5, 175), (77, 159), (108, 157.5)],
        [(108, 157.5), (140, 156), (140, 124), (166.5, 116)],
        [(166.5, 116), (192, 108.5), (194.5, 86.5), (218.5, 72.5)],
        [(218.5, 72.5), (237, 62), (256.5, 50), (276, 35.5)],
    ];

    private static readonly (double X, double Y, double Length)[] RouteSamples = SampleRoute(240);

    private readonly MobileSessionService session;
    private readonly OfflineMapService offlineMaps;
    private readonly OfflineRoutingBundleService offlineRouting;
    private readonly ApiClient apiClient;
    private readonly OfflineWebCache webCache;
    private readonly GpsTelemetryUploader telemetry;
    private bool started;
    private double shown;

    /// <summary>Raised once when start-up is done; true when a driver is signed in.</summary>
    public event EventHandler<bool>? Completed;

    public MainPage(
        MobileSessionService session,
        OfflineMapService offlineMaps,
        OfflineRoutingBundleService offlineRouting,
        ApiClient apiClient,
        OfflineWebCache webCache,
        GpsTelemetryUploader telemetry)
    {
        InitializeComponent();
        this.session = session;
        this.offlineMaps = offlineMaps;
        this.offlineRouting = offlineRouting;
        this.apiClient = apiClient;
        this.webCache = webCache;
        this.telemetry = telemetry;
        VersionLabel.Text = $"Verzija {AppInfo.Current.VersionString}";
        SetProgress(0);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartSceneAnimation();
        if (started) return;
        started = true;
        SafeUi.Run(RunAsync);
    }

    protected override void OnDisappearing()
    {
        this.AbortAnimation(SceneAnimation);
        this.AbortAnimation(ProgressAnimation);
        base.OnDisappearing();
    }

    private async Task RunAsync()
    {
        var signedIn = false;
        var online = false;

        await StepAsync("Pokretanje aplikacije…", 12, _ => Task.CompletedTask);
        await StepAsync("Provera prijave…", 32, async ct => signedIn = await session.RestoreAsync(ct));
        await StepAsync("Offline mape…", 52, ct => offlineMaps.GetInstalledManifestAsync(ct));
        await StepAsync("Paketi za rutiranje…", 70, ct => offlineRouting.GetInstalledManifestAsync(ct));
        await StepAsync("Veza sa serverom…", 90, async ct =>
        {
            online = await apiClient.IsServerReachableAsync(TimeSpan.FromSeconds(3), ct);
            if (!online)
            {
                webCache.MarkOffline();
            }
        });
        await StepAsync(online ? "Spremno" : "Spremno · rad bez veze", 100, _ => Task.CompletedTask);

        if (online)
        {
            // In the background: the navigation page for dead zones, positions queued offline.
            _ = Task.Run(webCache.WarmUpNavigationAsync);
            _ = Task.Run(telemetry.FlushAsync);
        }

        await Task.Delay(260);
        Completed?.Invoke(this, signedIn);
    }

    // The bar and the truck move while the step runs.
    private async Task StepAsync(string text, double target, Func<CancellationToken, Task> work)
    {
        StatusLabel.Text = text;
        var animation = AnimateProgressAsync(target, 360);

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

        // The truck drives Beograd → Novi Sad with the start-up.
        var (x, y) = PointAt(value / 100);
        Truck.TranslationX = x - TruckHalfSize;
        Truck.TranslationY = y - TruckHalfSize;
        DistanceLabel.Text = $"{Math.Max(0, Math.Round(RouteKilometres * (1 - value / 100)))} km";
    }

    // Dashes running along the route, the pulse ring round the truck and the live dot
    // (web: pmRouteDash, pmTruckPulse), in one loop.
    private void StartSceneAnimation()
    {
        this.AbortAnimation(SceneAnimation);
        new Animation
        {
            { 0, 1, new Animation(v => RouteLine.StrokeDashOffset = v, 0, -9) },
            { 0, 1, new Animation(v => { TruckPulse.Scale = 0.75 + v * 0.55; TruckPulse.Opacity = 0.9 * (1 - v); }, 0, 1, Easing.CubicOut) },
            { 0, 0.5, new Animation(v => LiveDot.Opacity = v, 1, 0.35) },
            { 0.5, 1, new Animation(v => LiveDot.Opacity = v, 0.35, 1) },
        }.Commit(this, SceneAnimation, 16, 1500, repeat: () => true);
    }

    /// <summary>Point at <paramref name="fraction"/> of the route length (even speed along the curve).</summary>
    private static (double X, double Y) PointAt(double fraction)
    {
        var total = RouteSamples[^1].Length;
        var target = Math.Clamp(fraction, 0, 1) * total;
        for (var i = 1; i < RouteSamples.Length; i++)
        {
            if (RouteSamples[i].Length >= target)
            {
                var a = RouteSamples[i - 1];
                var b = RouteSamples[i];
                var t = b.Length > a.Length ? (target - a.Length) / (b.Length - a.Length) : 0;
                return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
        }

        return (RouteSamples[^1].X, RouteSamples[^1].Y);
    }

    private static (double X, double Y, double Length)[] SampleRoute(int perSegment)
    {
        var samples = new List<(double X, double Y, double Length)> { (RouteSegments[0][0].X, RouteSegments[0][0].Y, 0) };
        foreach (var s in RouteSegments)
        {
            for (var i = 1; i <= perSegment; i++)
            {
                var t = (double)i / perSegment;
                var u = 1 - t;
                var x = u * u * u * s[0].X + 3 * u * u * t * s[1].X + 3 * u * t * t * s[2].X + t * t * t * s[3].X;
                var y = u * u * u * s[0].Y + 3 * u * u * t * s[1].Y + 3 * u * t * t * s[2].Y + t * t * t * s[3].Y;
                var previous = samples[^1];
                samples.Add((x, y, previous.Length + Math.Sqrt((x - previous.X) * (x - previous.X) + (y - previous.Y) * (y - previous.Y))));
            }
        }

        return [.. samples];
    }
}
