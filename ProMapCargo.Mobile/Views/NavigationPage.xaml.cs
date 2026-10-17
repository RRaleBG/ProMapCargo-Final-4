using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

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
    private readonly NavigationMapState mapState = new();

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

    protected override void OnAppearing()
    {
        base.OnAppearing();

        pageActive = true;
        webViewReady = false;
        // While guiding, the screen does not go dark (Podešavanja → Ekran uvek uključen).
        DeviceDisplay.Current.KeepScreenOn = AppSettings.KeepScreenOn;
        mapState.ResetLocation();
        ShowMapLoading();

        // Several regional maps: serve the one covering the truck (or the planned route).
        var where = viewModel.CurrentLocation ?? viewModel.RouteStartPoint;
        offlineWebCache.PreferredPoint = where is null ? null : (where.Lat, where.Lon);
        offlineWebCache.InvalidateLocalMap();
        NavigationWebView.Source = NavigationUrl;
        StartThenFillAnimation();

        SafeUi.Run(async () =>
        {
            await viewModel.LoadInitialRouteAsync();
            await PushStateToWebAsync(force: true);

            // "Kuda idemo?" or a recent destination tapped on the home screen.
            if (NavigationRequests.Take() is { } request)
            {
                await viewModel.ApplyRequestAsync(request);
            }
        });
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
        DeviceDisplay.Current.KeepScreenOn = false;

        base.OnDisappearing();
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        webViewReady = e.Result == WebNavigationResult.Success;
        mapState.ForceRouteResend();

        if (webViewReady)
        {
            LoadingOverlay.IsVisible = false;
            // While online, store the label fonts so names show offline too.
            _ = offlineWebCache.PrefetchGlyphsAsync();
            SafeUi.Run(async () =>
            {
                await ApplyEmbeddedMobileLayoutTweaksAsync();
                await notifier.ShowSuccessAsync("Mapa za navigaciju je spremna.");
                await PushStateToWebAsync(force: true);
            });
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
        // Without signal the stored copy is used; it exists once the app was online after
        // installation (start-up stores it), so this is the first start without a connection.
        LoadErrorText.Text = offlineWebCache.IsOffline
            ? "Nema veze sa serverom, a navigacija još nije sačuvana na telefonu. Pokrenite aplikaciju jednom " +
              "dok ste na mreži (sačuva se sama), uz instalirane offline mape."
            : $"Server {serverEndpointService.CurrentApiBaseUrl} nije dostupan. Proverite da li server radi i da li je " +
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
    private void OnRecenterClicked(object? sender, EventArgs e) => SafeUi.Run(RecenterAsync);

    private async Task RecenterAsync()
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

    // Confirmed first: a tap by mistake while driving must not drop the guidance.
    private void OnResetRouteClicked(object? sender, EventArgs e) => SafeUi.Run(ResetRouteAsync);

    private async Task ResetRouteAsync()
    {
        if (!viewModel.HasRoute ||
            !await DisplayAlertAsync("Poništiti rutu?", "Navođenje se zaustavlja, a odredište se briše sa mape.", "Poništi", "Otkaži"))
        {
            return;
        }

        viewModel.ResetRoute();
        await PushStateToWebAsync(force: true);
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

        if (mapState.Next(viewModel, force) is not { } update)
        {
            return;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var result = await NavigationWebView.EvaluateJavaScriptAsync(
                    $"window.proMapMobile?.applyState?.({update.Json}) ?? false;");

                if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                {
                    mapState.Delivered(update);
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
}
