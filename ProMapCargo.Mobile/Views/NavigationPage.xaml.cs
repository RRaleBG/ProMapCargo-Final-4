using Microsoft.Extensions.DependencyInjection;
using ProMapCargo.Mobile.ViewModels;
using System.Text.Json;

namespace ProMapCargo.Mobile.Views;

public partial class MobileNavigationPage : ContentPage
{
    private readonly NavigationViewModel viewModel;
    private bool pageActive;
    private bool webViewReady;

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
        LoadingOverlay.IsVisible = true;
        NavigationWebView.Source = NavigationUrl;

        await viewModel.LoadInitialRouteAsync();

        await PushStateToWebAsync();
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
        LoadingOverlay.IsVisible = false;
        webViewReady = e.Result == WebNavigationResult.Success;

        if (webViewReady)
        {
            await PushStateToWebAsync();
        }
    }

    private void OnRouteVisualizationChanged(object? sender, EventArgs e)
    {
        _ = PushStateToWebAsync();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(NavigationViewModel.CurrentLocation)
            or nameof(NavigationViewModel.RouteStartPoint)
            or nameof(NavigationViewModel.RouteDestinationPoint)
            or nameof(NavigationViewModel.StatusText)
            or nameof(NavigationViewModel.ErrorMessage))
        {
            _ = PushStateToWebAsync();
        }
    }

    private async Task PushStateToWebAsync()
    {
        if (!pageActive || !webViewReady)
        {
            return;
        }

        var payload = new
        {
            start = ToWebPoint(viewModel.RouteStartPoint),
            destination = ToWebPoint(viewModel.RouteDestinationPoint),
            current = ToWebPoint(viewModel.CurrentLocation),

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
                    latitude = maneuver.Latitude,
                    longitude = maneuver.Longitude
                })
                .ToArray(),

            restrictionCount = viewModel.RouteViolations.Count
        };

        var json = JsonSerializer.Serialize(payload);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var result = await NavigationWebView.EvaluateJavaScriptAsync(
                    $"window.proMapMobile?.applyState?.({json}) ?? false;");

                if (string.Equals(
                    result,
                    "true",
                    StringComparison.OrdinalIgnoreCase))
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

            await Task.Delay(250);
        }
    }

    private static object? ToWebPoint(
        ProMapCargo.Mobile.Models.GeoPoint? point)
    {
        return point is null
            ? null
            : new
            {
                latitude = point.Lat,
                longitude = point.Lon
            };
    }
}