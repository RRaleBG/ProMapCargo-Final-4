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
    private bool pageActive;
    private bool webViewReady;
    private bool routePopupOpen;
    private bool panelAnimating;
    private DateTimeOffset lastWebPushAt = DateTimeOffset.MinValue;
    private WebPoint? lastWebLocation;
    private DateTimeOffset? lastWebLocationAt;

    private string NavigationUrl =>
        DeviceInfo.Platform == DevicePlatform.Android
            ? "http://10.0.2.2:8080/navigation?embedded=1&mobile=1"
            : "http://localhost:8080/navigation?embedded=1&mobile=1";

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
        LoadingOverlay.IsVisible = true;

        NavigationWebView.Source = NavigationUrl;

        await viewModel.LoadInitialRouteAsync();
        await PushStateToWebAsync(force: true);
    }

    protected override void OnDisappearing()
    {
        pageActive = false;
        webViewReady = false;

        viewModel.StopGpsTracking();

        base.OnDisappearing();
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        webViewReady = e.Result == WebNavigationResult.Success;
        LoadingOverlay.IsVisible = !webViewReady;

        if (webViewReady)
        {
            await ApplyEmbeddedMobileLayoutTweaksAsync();
            await notifier.ShowSuccessAsync("Mapa za navigaciju je spremna.");
            await PushStateToWebAsync(force: true);
            return;
        }

        await notifier.ShowErrorAsync("Mapa za navigaciju nije učitana. Proverite da li server radi.");
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

        var payload = new
        {
            mobile = true,
            live = true,
            follow = true,
            start = ToWebPoint(viewModel.RouteStartPoint),
            destination = ToWebPoint(viewModel.RouteDestinationPoint),
            current,
            heading = motion.Heading,
            speedKph = motion.SpeedKph,
            route = viewModel.RoutePolylinePoints
                .Select(point => new
                {
                    latitude = point.Lat,
                    longitude = point.Lon
                })
                .ToArray(),
            maneuvers = viewModel.RouteManeuvers
                .Select(maneuver => new
                {
                    type = maneuver.Type,
                    instruction = maneuver.Instruction,
                    distanceMeters = maneuver.DistanceFromRouteStartMeters,
                    latitude = maneuver.Latitude,
                    longitude = maneuver.Longitude
                })
                .ToArray(),
            restrictionCount = viewModel.RouteViolations.Count
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
                RoutePopupPanel.IsVisible = true;
                RoutePopupPanel.InputTransparent = false;
                RoutePopupPanel.TranslationY = 28;
                RoutePopupPanel.Opacity = 0;

                await Task.WhenAll(
                    RoutePopupPanel.TranslateTo(0, 0, PanelEnterDurationMs, Easing.CubicOut),
                    RoutePopupPanel.FadeTo(1, PanelEnterDurationMs, Easing.CubicOut));

                return;
            }

            await Task.WhenAll(
                RoutePopupPanel.TranslateTo(0, 28, PanelExitDurationMs, Easing.CubicIn),
                RoutePopupPanel.FadeTo(0, PanelExitDurationMs, Easing.CubicIn));

            RoutePopupPanel.InputTransparent = true;
            RoutePopupPanel.IsVisible = false;
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
