using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class MobileNavigationPage : ContentPage
{
    private readonly NavigationViewModel viewModel;
    private readonly WebView mapWebView;

    public MobileNavigationPage(NavigationViewModel viewModel, IOptions<MobileAppOptions> appOptions)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.viewModel = viewModel;

        var apiBaseUrl = appOptions.Value.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new ArgumentException("API base URL is not configured.", nameof(appOptions));
        }

        var normalizedBaseUrl = apiBaseUrl.TrimEnd('/');

        mapWebView = new WebView
        {
            Source = new UrlWebViewSource
            {
                Url = $"{normalizedBaseUrl}/Navigation?embedded=1"
            }
        };

        MapHost.Children.Add(mapWebView);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.RouteVisualizationChanged += HandleRouteVisualizationChanged;
        await viewModel.LoadInitialRouteAsync();
        await PushNavigationStateToWebMapAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.RouteVisualizationChanged -= HandleRouteVisualizationChanged;
        viewModel.StopGpsTracking();
        base.OnDisappearing();
    }

    private async void HandleRouteVisualizationChanged(object? sender, EventArgs e)
    {
        await PushNavigationStateToWebMapAsync();
    }

    private async Task PushNavigationStateToWebMapAsync()
    {
        var payload = new
        {
            route = viewModel.RoutePolylinePoints.Select(point => new { lat = point.Lat, lon = point.Lon }).ToArray(),
            start = viewModel.RouteStartPoint is null ? null : new { lat = viewModel.RouteStartPoint.Lat, lon = viewModel.RouteStartPoint.Lon },
            destination = viewModel.RouteDestinationPoint is null ? null : new { lat = viewModel.RouteDestinationPoint.Lat, lon = viewModel.RouteDestinationPoint.Lon },
            current = viewModel.CurrentLocation is null ? null : new { lat = viewModel.CurrentLocation.Lat, lon = viewModel.CurrentLocation.Lon },
            layers = new
            {
                route = viewModel.ShowRouteLayer,
                restrictions = viewModel.ShowRestrictionsLayer,
                fleet = viewModel.ShowFleetLayer,
                poi = viewModel.ShowPoiLayer
            },
            maneuvers = viewModel.RouteManeuvers.Take(20).Select(maneuver => new
            {
                type = maneuver.Type,
                instruction = maneuver.Instruction,
                lat = maneuver.Latitude,
                lon = maneuver.Longitude
            }).ToArray(),
            restrictionCount = viewModel.RouteViolations.Count
        };

        var json = JsonSerializer.Serialize(payload);
        var encodedJson = JavaScriptEncoder.Default.Encode(json);

        var script = $"window.proMapMobile && window.proMapMobile.applyState && window.proMapMobile.applyState(JSON.parse('{encodedJson}'));";

        try
        {
            await mapWebView.EvaluateJavaScriptAsync(script);
        }
        catch
        {
        }
    }
}
