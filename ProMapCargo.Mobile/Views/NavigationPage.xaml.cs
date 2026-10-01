using Microsoft.Extensions.DependencyInjection;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.ViewModels;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Views;

public partial class MobileNavigationPage : ContentPage
{
    private readonly NavigationViewModel viewModel;
    private bool pageActive;
    private bool webViewReady;
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

        viewModel = Application.Current?.Handler?.MauiContext?.Services
            .GetRequiredService<NavigationViewModel>()
            ?? throw new InvalidOperationException(
                "NavigationViewModel nije registrovan u MAUI DI kontejneru.");

        BindingContext = viewModel;

        NavigationWebView.Navigated += OnWebViewNavigated;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.RouteVisualizationChanged += OnRouteVisualizationChanged;
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
            await PushStateToWebAsync(force: true);
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

        var heading = distanceMeters >= 2
            ? InitialBearing(
                lastWebLocation.Latitude,
                lastWebLocation.Longitude,
                current.Latitude,
                current.Longitude)
            : null;

        var speedKph = elapsedSeconds > 0.5 && distanceMeters >= 1
            ? distanceMeters / elapsedSeconds * 3.6
            : null;

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
