using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class MobileNavigationPage : ContentPage
{
    private readonly NavigationViewModel viewModel;
    private readonly string navigationUrl;

    private bool webViewReady;
    private bool isAppearing;

    public MobileNavigationPage(
        NavigationViewModel viewModel,
        IOptions<MobileAppOptions> appOptions)
    {
        InitializeComponent();

        BindingContext = viewModel;
        this.viewModel = viewModel;

        var apiBaseUrl = appOptions.Value.ApiBaseUrl;

        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new ArgumentException(
                "API base URL is not configured.",
                nameof(appOptions));
        }

        var normalizedBaseUrl = apiBaseUrl.TrimEnd('/');

        navigationUrl =
            $"{normalizedBaseUrl}/Navigation?embedded=1&mobile=1";

        NavigationWebView.Navigated += NavigationWebView_Navigated;
        NavigationWebView.Navigating += NavigationWebView_Navigating;

        NavigationWebView.Source = new UrlWebViewSource
        {
            Url = navigationUrl
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        isAppearing = true;

        viewModel.RouteVisualizationChanged -= HandleRouteVisualizationChanged;
        viewModel.RouteVisualizationChanged += HandleRouteVisualizationChanged;

        try
        {
            await viewModel.LoadInitialRouteAsync();

            if (webViewReady)
            {
                await PushNavigationStateToWebMapAsync();
            }
        }
        catch
        {
            // Nemoj rušiti navigacionu stranicu ako početni route/state
            // trenutno nije dostupan.
        }
    }

    protected override void OnDisappearing()
    {
        isAppearing = false;

        viewModel.RouteVisualizationChanged -= HandleRouteVisualizationChanged;
        viewModel.StopGpsTracking();

        base.OnDisappearing();
    }

    private async void NavigationWebView_Navigated(
        object? sender,
        WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            LoadingOverlay.IsVisible = false;
            return;
        }

        webViewReady = true;

        LoadingOverlay.IsVisible = false;

        // Daj stranici jedan UI frame da registruje
        // window.proMapMobile pre slanja state-a.
        await Task.Delay(50);

        if (isAppearing)
        {
            await PushNavigationStateToWebMapAsync();
        }
    }

    private void NavigationWebView_Navigating(
        object? sender,
        WebNavigatingEventArgs e)
    {
        // Za sada dozvoljavamo navigaciju unutar web aplikacije.
        // Kasnije ovde možemo dodati native URL interception
        // za tel:, mailto:, geo: itd.
    }

    private async void HandleRouteVisualizationChanged(
        object? sender,
        EventArgs e)
    {
        if (!webViewReady || !isAppearing)
        {
            return;
        }

        await PushNavigationStateToWebMapAsync();
    }

    private async Task PushNavigationStateToWebMapAsync()
    {
        if (!webViewReady)
        {
            return;
        }

        var payload = BuildWebPayloadJson();

        var json = payload.ToJsonString(
            new JsonSerializerOptions
            {
                WriteIndented = false
            });

        var encodedJson = JavaScriptEncoder.Default.Encode(json);

        var script =
            $"window.proMapMobile?.applyState?.(" +
            $"JSON.parse('{encodedJson}')" +
            $");";

        try
        {
            await NavigationWebView.EvaluateJavaScriptAsync(script);
        }
        catch
        {
            // WebView može biti u procesu reload-a/dispose-a.
        }
    }

    private JsonObject BuildWebPayloadJson()
    {
        var route = new JsonArray();

        foreach (var point in viewModel.RoutePolylinePoints)
        {
            route.Add(new JsonObject
            {
                ["lat"] = point.Lat,
                ["lon"] = point.Lon
            });
        }

        var maneuvers = new JsonArray();

        foreach (var maneuver in viewModel.RouteManeuvers.Take(20))
        {
            maneuvers.Add(new JsonObject
            {
                ["type"] = maneuver.Type,
                ["instruction"] = maneuver.Instruction,
                ["lat"] = maneuver.Latitude,
                ["lon"] = maneuver.Longitude,
                ["distanceFromPreviousMeters"] = maneuver.DistanceFromPreviousMeters,
                ["distanceFromRouteStartMeters"] = maneuver.DistanceFromRouteStartMeters,
                ["roadName"] = maneuver.RoadName,
                ["roadRef"] = maneuver.RoadRef,
                ["roundaboutExit"] = maneuver.RoundaboutExit
            });
        }

        JsonNode? start = null;

        if (viewModel.RouteStartPoint is { } startPoint)
        {
            start = new JsonObject
            {
                ["lat"] = startPoint.Lat,
                ["lon"] = startPoint.Lon
            };
        }

        JsonNode? destination = null;

        if (viewModel.RouteDestinationPoint is { } destinationPoint)
        {
            destination = new JsonObject
            {
                ["lat"] = destinationPoint.Lat,
                ["lon"] = destinationPoint.Lon
            };
        }

        JsonNode? current = null;

        if (viewModel.CurrentLocation is { } currentPoint)
        {
            current = new JsonObject
            {
                ["lat"] = currentPoint.Lat,
                ["lon"] = currentPoint.Lon
            };
        }

        return new JsonObject
        {
            ["route"] = route,
            ["start"] = start,
            ["destination"] = destination,
            ["current"] = current,

            ["layers"] = new JsonObject
            {
                ["route"] = viewModel.ShowRouteLayer,
                ["restrictions"] = viewModel.ShowRestrictionsLayer,
                ["fleet"] = viewModel.ShowFleetLayer,
                ["poi"] = viewModel.ShowPoiLayer
            },

            ["maneuvers"] = maneuvers,

            ["restrictionCount"] =
                viewModel.RouteViolations.Count
        };
    }
}
