using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Views;

public partial class MobileNavigationPage : ContentPage
{
    private const uint PanelEnterDurationMs = 220;
    private const uint PanelExitDurationMs = 180;

    private readonly NavigationViewModel viewModel;
    private readonly IMobileNotifier notifier;
    private readonly MobileServerEndpointService serverEndpointService;
    private readonly OfflineWebCache offlineWebCache;
    private bool pageActive;
    private int mapLoadAttempt;
    private bool webViewReady;
    private bool routePopupOpen;
    private bool panelAnimating;
    private DateTimeOffset lastWebPushAt = DateTimeOffset.MinValue;
    // Route last delivered to the web map; empty forces a full resend (page load).
    private string lastSentRouteKey = string.Empty;
    private WebPoint? lastWebLocation;
    private DateTimeOffset? lastWebLocationAt;

    private string NavigationUrl => serverEndpointService.NavigationUrl;

    public MobileNavigationPage()
    {
        InitializeComponent();

        var services = Application.Current?.Handler?.MauiContext?.Services
            ?? IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("MAUI service provider is unavailable.");

        viewModel = services
            .GetRequiredService<NavigationViewModel>();

        notifier = services
            .GetRequiredService<IMobileNotifier>();

        serverEndpointService = services
            .GetRequiredService<MobileServerEndpointService>();

        offlineWebCache = services
            .GetRequiredService<OfflineWebCache>();

        BindingContext = viewModel;

        NavigationWebView.Navigated += OnWebViewNavigated;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.RouteVisualizationChanged += OnRouteVisualizationChanged;

        RoutePopupPanel.IsVisible = false;
        RoutePopupPanel.Opacity = 0;
        RoutePopupPanel.TranslationY = 28;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        pageActive = true;
        webViewReady = false;
        lastWebLocation = null;
        lastWebLocationAt = null;
        ShowMapLoading();

        // Several regional maps: serve the one covering the truck (or the planned route).
        var where = viewModel.CurrentLocation ?? viewModel.RouteStartPoint;
        offlineWebCache.PreferredPoint = where is null ? null : (where.Lat, where.Lon);
        offlineWebCache.InvalidateLocalMap();
        NavigationWebView.Source = NavigationUrl;
        StartThenFillAnimation();

        await viewModel.LoadInitialRouteAsync();
        await PushStateToWebAsync(force: true);

        // "Kuda idemo?" or a recent destination tapped on the home screen.
        if (NavigationRequests.Take() is { } request)
        {
            await viewModel.ApplyRequestAsync(request);
        }
    }

    private const string ThenFillAnimationName = "ThenFill";

    // The "zatim" tile fills with Brand from the bottom and drains again, in a loop,
    // so the following maneuver catches the eye without words.
    private void StartThenFillAnimation()
    {
        this.AbortAnimation(ThenFillAnimationName);

        const double tileHeight = 40;
        var loop = new Animation
        {
            { 0, 0.5, new Animation(v => ThenFill.HeightRequest = v, 0, tileHeight, Easing.SinInOut) },
            { 0.5, 1, new Animation(v => ThenFill.HeightRequest = v, tileHeight, 0, Easing.SinInOut) },
        };

        loop.Commit(this, ThenFillAnimationName, 16, 2400, repeat: () => pageActive);
    }

    protected override void OnDisappearing()
    {
        this.AbortAnimation(ThenFillAnimationName);
        pageActive = false;
        webViewReady = false;

        viewModel.StopGpsTracking();

        base.OnDisappearing();
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        webViewReady = e.Result == WebNavigationResult.Success;
        lastSentRouteKey = string.Empty;

        if (webViewReady)
        {
            LoadingOverlay.IsVisible = false;
            // While online, store the label fonts so names show offline too.
            _ = offlineWebCache.PrefetchGlyphsAsync();
            await ApplyEmbeddedMobileLayoutTweaksAsync();
            await notifier.ShowSuccessAsync("Mapa za navigaciju je spremna.");
            await PushStateToWebAsync(force: true);
            return;
        }

        ShowMapLoadError();
    }

    // The full-screen loading overlay takes every tap; if the map never loads (e.g.
    // a phone still pointing at the emulator address 10.0.2.2) it must turn into an
    // error with a way out, or no button on this screen works.
    private static readonly TimeSpan MapLoadTimeout = TimeSpan.FromSeconds(20);

    private void ShowMapLoading()
    {
        LoadingOverlay.IsVisible = true;
        LoadingPanel.IsVisible = true;
        LoadErrorPanel.IsVisible = false;

        var attempt = ++mapLoadAttempt;
        Dispatcher.DispatchDelayed(MapLoadTimeout, () =>
        {
            if (pageActive && !webViewReady && attempt == mapLoadAttempt)
            {
                ShowMapLoadError();
            }
        });
    }

    private void ShowMapLoadError()
    {
        LoadingOverlay.IsVisible = true;
        LoadingPanel.IsVisible = false;
        LoadErrorPanel.IsVisible = true;
        LoadErrorText.Text =
            $"Server {serverEndpointService.CurrentApiBaseUrl} nije dostupan. Proverite da li server radi i da li je " +
            "telefon na istoj mreži. Na telefonu unesite IP adresu računara (npr. http://192.168.1.10:8080/), " +
            "adresa 10.0.2.2 radi samo u emulatoru.";
    }

    private void OnRetryMapClicked(object? sender, EventArgs e)
    {
        webViewReady = false;
        ShowMapLoading();
        // A new URL forces a real reload even when the address did not change.
        NavigationWebView.Source = $"{NavigationUrl}&t={DateTime.UtcNow.Ticks}";
    }

    private async void OnOpenServerSettingsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Settings);

    private void OnSuggestionClicked(object? sender, EventArgs e)
    {
        var suggestion = (sender as BindableObject)?.BindingContext as GeocodeResult;

        if (suggestion is null)
        {
            return;
        }

        if (viewModel.SelectSuggestionCommand.CanExecute(suggestion))
        {
            viewModel.SelectSuggestionCommand.Execute(suggestion);
        }
    }

    // TomTom-style recenter: snap the camera back to the truck and resume following
    // (a pan or pinch on the map pauses it). Rerouting stays automatic when off route.
    private async void OnRecenterClicked(object? sender, EventArgs e)
    {
        if (!webViewReady)
        {
            return;
        }

        if (!await viewModel.EnsureCurrentLocationAsync(showWarnings: true))
        {
            return;
        }

        await PushStateToWebAsync(force: true);

        try
        {
            var result = await NavigationWebView.EvaluateJavaScriptAsync("window.proMapMobile?.recenter?.() ?? false;");
            if (!string.Equals(result, "true", StringComparison.OrdinalIgnoreCase) && viewModel.CurrentLocation is { } current)
            {
                await NavigationWebView.EvaluateJavaScriptAsync(
                    $"window.proMapMobile?.updateLocation?.({{ latitude: {current.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, longitude: {current.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}, follow: true }}) ?? false;");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recenter failed: {ex.Message}");
            await notifier.ShowInfoAsync("Centriranje nije uspelo. Sačekajte GPS lokaciju i pokušajte ponovo.");
        }
    }

    private void OnRouteVisualizationChanged(object? sender, EventArgs e)
    {
        _ = PushStateToWebAsync(force: true);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(NavigationViewModel.CurrentLocation)
            or nameof(NavigationViewModel.RouteStartPoint)
            or nameof(NavigationViewModel.RouteDestinationPoint)
            or nameof(NavigationViewModel.NextManeuverIndex)
            or nameof(NavigationViewModel.NextManeuverDistanceText)
            or nameof(NavigationViewModel.StatusText)
            or nameof(NavigationViewModel.ErrorMessage))
        {
            _ = PushStateToWebAsync();
        }

        // GPS or the route arrived after the page opened: if another installed region covers
        // it, reload the map from that region (else the map stays empty there).
        if (e.PropertyName is nameof(NavigationViewModel.CurrentLocation) or nameof(NavigationViewModel.RouteStartPoint) &&
            (viewModel.CurrentLocation ?? viewModel.RouteStartPoint) is { } where &&
            offlineWebCache.SelectMapFor(where.Lat, where.Lon))
        {
            OnRetryMapClicked(null, EventArgs.Empty);
        }

        if (e.PropertyName == nameof(NavigationViewModel.IsRouteInputVisible))
        {
            _ = ToggleRoutePopupAsync(viewModel.IsRouteInputVisible);
        }
    }

    private async Task PushStateToWebAsync(bool force = false)
    {
        if (!pageActive || !webViewReady)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - lastWebPushAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        lastWebPushAt = now;

        var current = ToWebPoint(viewModel.CurrentLocation);
        var motion = CalculateMotion(current);

        var points = viewModel.RoutePolylinePoints;
        var routeKey = points.Count == 0
            ? ""
            : $"{points.Count}:{points[0].Lat},{points[0].Lon}:{points[^1].Lat},{points[^1].Lon}:{viewModel.RouteManeuvers.Count}";
        var includeRoute = routeKey != lastSentRouteKey;

        var payload = new
        {
            mobile = true,
            live = true,
            follow = true,
            start = ToWebPoint(viewModel.RouteStartPoint),
            destination = ToWebPoint(viewModel.RouteDestinationPoint),
            current,
            heading = motion.Heading,
            // Same filtered speed the bubble shows; drives the speed-dependent zoom.
            speedKph = double.TryParse(viewModel.CurrentSpeedText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var shownSpeed)
                ? shownSpeed
                : motion.SpeedKph,
            // Route and maneuvers (thousands of points) only when they changed; the
            // map keeps the last ones. Sending them on every GPS fix made it stutter.
            route = includeRoute
                ? viewModel.RoutePolylinePoints
                    .Select(point => new
                    {
                        latitude = point.Lat,
                        longitude = point.Lon
                    })
                    .ToArray()
                : null,
            maneuvers = includeRoute
                ? viewModel.RouteManeuvers
                    .Select(maneuver => new
                    {
                        type = maneuver.Type,
                        instruction = maneuver.Instruction,
                        distanceMeters = maneuver.DistanceFromRouteStartMeters,
                        latitude = maneuver.Latitude,
                        longitude = maneuver.Longitude
                    })
                    .ToArray()
                : null,
            restrictionCount = viewModel.RouteViolations.Count,
            // NextManeuverIndex is 1-based; the web map marks the upcoming turn.
            nextManeuverIndex = Math.Max(0, viewModel.NextManeuverIndex - 1)
        };

        var json = JsonSerializer.Serialize(payload);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var result = await NavigationWebView.EvaluateJavaScriptAsync(
                    $"window.proMapMobile?.applyState?.({json}) ?? false;");

                if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                {
                    if (includeRoute)
                    {
                        lastSentRouteKey = routeKey;
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Mobile navigation bridge error: {ex.Message}");
            }

            if (!pageActive)
            {
                return;
            }

            await Task.Delay(150);
        }
    }

    private async Task ToggleRoutePopupAsync(bool open)
    {
        if (panelAnimating || routePopupOpen == open)
        {
            return;
        }

        panelAnimating = true;

        try
        {
            if (open)
            {
                routePopupOpen = true;
                // The speed bubble and buttons would show through and catch taps next to the sheet.
                FloatingControls.IsVisible = false;
                RoutePopupPanel.IsVisible = true;
                RoutePopupPanel.InputTransparent = false;
                RoutePopupPanel.TranslationY = 28;
                RoutePopupPanel.Opacity = 0;

                await Task.WhenAll(
                    RoutePopupPanel.TranslateToAsync(0, 0, PanelEnterDurationMs, Easing.CubicOut),
                    RoutePopupPanel.FadeToAsync(1, PanelEnterDurationMs, Easing.CubicOut));

                return;
            }

            // Closing the sheet (also after a route is calculated) hides the keyboard.
            foreach (var entry in new[] { StartEntry, DestinationEntry })
            {
                if (entry.IsSoftInputShowing())
                {
                    await entry.HideSoftInputAsync(CancellationToken.None);
                }

                entry.Unfocus();
            }

            await Task.WhenAll(
                RoutePopupPanel.TranslateToAsync(0, 28, PanelExitDurationMs, Easing.CubicIn),
                RoutePopupPanel.FadeToAsync(0, PanelExitDurationMs, Easing.CubicIn));

            RoutePopupPanel.InputTransparent = true;
            RoutePopupPanel.IsVisible = false;
            FloatingControls.IsVisible = true;
            routePopupOpen = false;
        }
        finally
        {
            panelAnimating = false;
        }
    }

    private async Task ApplyEmbeddedMobileLayoutTweaksAsync()
    {
        if (!webViewReady)
        {
            return;
        }

        const string cleanupScript = "(function(){try{var selectors=['.route-planner','.route-input','.route-form','.route-card','.route-panel','.mobile-route-panel','.pm-route-panel','.panel-bottom','.navigation-bottom','.planner-card','.planner-panel'];selectors.forEach(function(sel){document.querySelectorAll(sel).forEach(function(el){if(el){el.style.display='none';el.style.visibility='hidden';el.style.opacity='0';el.style.pointerEvents='none';}});});var mapHosts=['#map','.map-container','.leaflet-container','.navigation-map'];mapHosts.forEach(function(sel){document.querySelectorAll(sel).forEach(function(el){if(el){el.style.bottom='0';el.style.height='100%';}});});return true;}catch(e){return false;}})();";

        try
        {
            await NavigationWebView.EvaluateJavaScriptAsync(cleanupScript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Mobile embedded layout tweak failed: {ex.Message}");
        }
    }

    private MotionSnapshot CalculateMotion(WebPoint? current)
    {
        if (current is null)
        {
            return new MotionSnapshot(null, null);
        }

        if (lastWebLocation is null || lastWebLocationAt is null)
        {
            lastWebLocation = current;
            lastWebLocationAt = DateTimeOffset.UtcNow;
            return new MotionSnapshot(null, null);
        }

        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = (now - lastWebLocationAt.Value).TotalSeconds;
        var distanceMeters = HaversineMeters(
            lastWebLocation.Latitude,
            lastWebLocation.Longitude,
            current.Latitude,
            current.Longitude);

        double? heading = null;
        if (distanceMeters >= 2)
        {
            heading = InitialBearing(
                lastWebLocation.Latitude,
                lastWebLocation.Longitude,
                current.Latitude,
                current.Longitude);
        }

        double? speedKph = null;
        if (elapsedSeconds > 0.5 && distanceMeters >= 1)
        {
            speedKph = distanceMeters / elapsedSeconds * 3.6;
        }

        lastWebLocation = current;
        lastWebLocationAt = now;

        return new MotionSnapshot(heading, speedKph);
    }

    private static double HaversineMeters(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        const double earthRadius = 6371000;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(DegreesToRadians(lat1)) *
            Math.Cos(DegreesToRadians(lat2)) *
            Math.Sin(dLon / 2) *
            Math.Sin(dLon / 2);

        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double InitialBearing(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        var firstLatitude = DegreesToRadians(lat1);
        var secondLatitude = DegreesToRadians(lat2);
        var deltaLongitude = DegreesToRadians(lon2 - lon1);

        var y = Math.Sin(deltaLongitude) * Math.Cos(secondLatitude);
        var x =
            Math.Cos(firstLatitude) * Math.Sin(secondLatitude) -
            Math.Sin(firstLatitude) *
            Math.Cos(secondLatitude) *
            Math.Cos(deltaLongitude);

        return (RadiansToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;

    private static double RadiansToDegrees(double value) => value * 180 / Math.PI;

    private static WebPoint? ToWebPoint(GeoPoint? point)
    {
        return point is null
            ? null
            : new WebPoint(point.Lat, point.Lon);
    }

    private sealed record MotionSnapshot(double? Heading, double? SpeedKph);

    private sealed record WebPoint(
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude);
}
