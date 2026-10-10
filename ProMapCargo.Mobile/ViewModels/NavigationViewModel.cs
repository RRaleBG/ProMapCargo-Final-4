using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;

namespace ProMapCargo.Mobile.ViewModels;

public class NavigationViewModel : ViewModelBase
{
    private const double OffRouteThresholdMeters = 120;
    private const double ManeuverArrivalThresholdMeters = 35;
    private static readonly TimeSpan GpsPollInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan AutoRerouteCooldown = TimeSpan.FromSeconds(20);
    private readonly ApiClient apiClient;
    private readonly OfflineRoutingBundleService offlineRoutingBundleService;
    private readonly IMobileNotifier notifier;
    private CancellationTokenSource? gpsTrackingCts;
    private DateTimeOffset lastAutoRerouteAt = DateTimeOffset.MinValue;
    private string startLatitude = string.Empty;
    private string startLongitude = string.Empty;
    private string destinationLatitude = string.Empty;
    private string destinationLongitude = string.Empty;
    private string profile = "truck";
    private bool avoidRestricted = true;
    private bool isBusy;
    private bool isRouteInputVisible;
    private bool autoRerouteEnabled = true;
    private bool voiceGuidanceEnabled = true;
    private bool showRouteLayer = true;
    private bool showRestrictionsLayer = true;
    private bool showFleetLayer = true;
    private bool showPoiLayer = true;
    private bool isOffRoute;
    private bool isSpeakingPrompt;
    private double offRouteDistanceMeters;
    private int nextManeuverIndex;
    private double nextManeuverProgress;
    private double maneuverInitialDistanceMeters = 1;
    private int? lastSpokenManeuverIndex;
    private int lastSpokenDistanceBucket = -1;
    private Locale? serbianLocale;
    private string statusText = "Spremno za izračunavanje rute";
    private string? errorMessage;
    private string routeCode = "—";
    private string routeSummary = "Ruta još nije izračunata.";
    private string routeEngine = "Ruter: —";
    private string routeSafety = "Provera za kamion u toku";
    private string routeDiagnosticsSummary = "Nema dijagnostike.";
    private string routeStartSnap = "—";
    private string routeEndSnap = "—";
    private string routeFailureReason = "—";
    private string routeEstimatedArrival = "—";
    private string currentLocationText = "GPS: waiting for fix";
    private string selectedRoutingMode = "Auto";
    private string activeRoutingModeText = "Režim rutiranja: online";
    private bool offlineRoutingAvailable;
    private string offRouteStatusText = "Off-route monitoring inactive";
    private string nextManeuverInstruction = "Izračunajte rutu za početak navođenja";
    private string nextManeuverDistanceText = "—";
    private string nextManeuverType = "Nastavite pravo";
    private string nextManeuverSymbol = "↑";
    private GeoPoint? routeStartPoint;
    private GeoPoint? routeDestinationPoint;
    private GeoPoint? currentLocation;
    private string startQuery = string.Empty;
    private string destinationQuery = string.Empty;
    private string activeField = "destination";
    private bool suppressSearch;
    private CancellationTokenSource? searchCts;
    private string currentSpeedText = "0";
    private bool hasRoute;

    public NavigationViewModel(ApiClient apiClient, OfflineRoutingBundleService offlineRoutingBundleService, IMobileNotifier notifier)
    {
        this.apiClient = apiClient;
        this.offlineRoutingBundleService = offlineRoutingBundleService;
        this.notifier = notifier;

        RouteHighlights = [];
        RouteManeuvers = [];
        RouteViolations = [];
        RoutePolylinePoints = [];
        LaneRibbon = [];

        CalculateRouteCommand = new Command(async () => await CalculateRouteAsync(), () => !IsBusy);
        ToggleRouteInputCommand = new Command(ToggleRouteInput);
        RerouteCommand = new Command(async () => await RerouteFromCurrentLocationAsync(), () => !IsBusy);
        SpeakNextPromptCommand = new Command(async () => await SpeakNextPromptAsync(force: true));

        RoutingModes = ["Auto", "Online", "Offline"];

        Suggestions = [];
        SelectSuggestionCommand = new Command<GeocodeResult>(SelectSuggestion);
        UseMyLocationCommand = new Command(UseMyLocation);
        SwapPointsCommand = new Command(SwapPoints);
    }

    public ObservableCollection<GeocodeResult> Suggestions { get; }

    public ICommand SelectSuggestionCommand { get; }

    public ICommand UseMyLocationCommand { get; }

    public ICommand SwapPointsCommand { get; }

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasRoute
    {
        get => hasRoute;
        private set => SetProperty(ref hasRoute, value);
    }

    public string CurrentSpeedText
    {
        get => currentSpeedText;
        private set => SetProperty(ref currentSpeedText, value);
    }

    public string StartQuery
    {
        get => startQuery;
        set
        {
            if (!SetProperty(ref startQuery, value)) return;
            if (suppressSearch) return;
            StartLatitude = string.Empty;
            StartLongitude = string.Empty;
            activeField = "start";
            _ = SearchAsync(value);
        }
    }

    public string DestinationQuery
    {
        get => destinationQuery;
        set
        {
            if (!SetProperty(ref destinationQuery, value)) return;
            if (suppressSearch) return;
            DestinationLatitude = string.Empty;
            DestinationLongitude = string.Empty;
            activeField = "destination";
            _ = SearchAsync(value);
        }
    }

    private async Task SearchAsync(string query)
    {
        searchCts?.Cancel();
        var cts = searchCts = new CancellationTokenSource();

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            ReplaceItems(Suggestions, []);
            RaisePropertyChanged(nameof(HasSuggestions));
            return;
        }

        try
        {
            await Task.Delay(350, cts.Token);
            var results = await apiClient.GeocodeAsync(query.Trim(), cts.Token);
            if (cts.IsCancellationRequested) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ReplaceItems(Suggestions, results);
                RaisePropertyChanged(nameof(HasSuggestions));
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ReplaceItems(Suggestions, []);
                RaisePropertyChanged(nameof(HasSuggestions));
            });
        }
    }

    private void SelectSuggestion(GeocodeResult? item)
    {
        if (item is null) return;
        suppressSearch = true;
        var lat = item.Lat.ToString("0.000000", CultureInfo.InvariantCulture);
        var lon = item.Lon.ToString("0.000000", CultureInfo.InvariantCulture);
        if (activeField == "start")
        {
            StartQuery = item.Title;
            StartLatitude = lat;
            StartLongitude = lon;
        }
        else
        {
            DestinationQuery = item.Title;
            DestinationLatitude = lat;
            DestinationLongitude = lon;
        }
        suppressSearch = false;
        ReplaceItems(Suggestions, []);
        RaisePropertyChanged(nameof(HasSuggestions));
    }

    private void UseMyLocation()
    {
        if (CurrentLocation is null)
        {
            ErrorMessage = "GPS lokacija još nije dostupna.";
            return;
        }
        suppressSearch = true;
        StartQuery = "Moja lokacija";
        StartLatitude = CurrentLocation.Lat.ToString("0.000000", CultureInfo.InvariantCulture);
        StartLongitude = CurrentLocation.Lon.ToString("0.000000", CultureInfo.InvariantCulture);
        suppressSearch = false;
        ReplaceItems(Suggestions, []);
        RaisePropertyChanged(nameof(HasSuggestions));
    }

    private void SwapPoints()
    {
        suppressSearch = true;
        (StartQuery, DestinationQuery) = (DestinationQuery, StartQuery);
        (StartLatitude, DestinationLatitude) = (DestinationLatitude, StartLatitude);
        (StartLongitude, DestinationLongitude) = (DestinationLongitude, StartLongitude);
        suppressSearch = false;
    }

    public event EventHandler? RouteVisualizationChanged;

    public ObservableCollection<string> RouteHighlights { get; }

    public ObservableCollection<RouteManeuver> RouteManeuvers { get; }

    public ObservableCollection<RestrictionViolation> RouteViolations { get; }

    public ObservableCollection<GeoPoint> RoutePolylinePoints { get; }

    public ObservableCollection<LaneCue> LaneRibbon { get; }

    public IReadOnlyList<string> RoutingModes { get; }

    public ICommand CalculateRouteCommand { get; }

    public ICommand ToggleRouteInputCommand { get; }

    public ICommand RerouteCommand { get; }

    public ICommand SpeakNextPromptCommand { get; }

    public string StartLatitude
    {
        get => startLatitude;
        set => SetProperty(ref startLatitude, value);
    }

    public string StartLongitude
    {
        get => startLongitude;
        set => SetProperty(ref startLongitude, value);
    }

    public string DestinationLatitude
    {
        get => destinationLatitude;
        set => SetProperty(ref destinationLatitude, value);
    }

    public string DestinationLongitude
    {
        get => destinationLongitude;
        set => SetProperty(ref destinationLongitude, value);
    }

    public string Profile
    {
        get => profile;
        set => SetProperty(ref profile, value);
    }

    public bool AvoidRestricted
    {
        get => avoidRestricted;
        set => SetProperty(ref avoidRestricted, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value))
            {
                return;
            }

            if (CalculateRouteCommand is Command calculateCommand)
            {
                calculateCommand.ChangeCanExecute();
            }

            if (RerouteCommand is Command rerouteCommand)
            {
                rerouteCommand.ChangeCanExecute();
            }
        }
    }

    public bool IsRouteInputVisible
    {
        get => isRouteInputVisible;
        private set
        {
            if (SetProperty(ref isRouteInputVisible, value))
            {
                RaisePropertyChanged(nameof(RouteInputButtonText));
            }
        }
    }

    public string RouteInputButtonText => IsRouteInputVisible ? "Sakrij planer" : "Planiraj rutu";

    public bool AutoRerouteEnabled
    {
        get => autoRerouteEnabled;
        set => SetProperty(ref autoRerouteEnabled, value);
    }

    public string SelectedRoutingMode
    {
        get => selectedRoutingMode;
        set
        {
            if (SetProperty(ref selectedRoutingMode, value))
            {
                var effectiveMode = ResolveEffectiveRoutingMode();
                ActiveRoutingModeText = OfflineRoutingAvailable
                    ? $"Režim rutiranja: {effectiveMode} (offline paket spreman)"
                    : $"Režim rutiranja: {effectiveMode} (offline paket nije instaliran)";
            }
        }
    }

    public string ActiveRoutingModeText
    {
        get => activeRoutingModeText;
        private set => SetProperty(ref activeRoutingModeText, value);
    }

    public bool OfflineRoutingAvailable
    {
        get => offlineRoutingAvailable;
        private set => SetProperty(ref offlineRoutingAvailable, value);
    }

    public bool VoiceGuidanceEnabled
    {
        get => voiceGuidanceEnabled;
        set => SetProperty(ref voiceGuidanceEnabled, value);
    }

    public bool ShowRouteLayer
    {
        get => showRouteLayer;
        set
        {
            if (SetProperty(ref showRouteLayer, value))
            {
                RouteVisualizationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowRestrictionsLayer
    {
        get => showRestrictionsLayer;
        set
        {
            if (SetProperty(ref showRestrictionsLayer, value))
            {
                RouteVisualizationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowFleetLayer
    {
        get => showFleetLayer;
        set
        {
            if (SetProperty(ref showFleetLayer, value))
            {
                RouteVisualizationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowPoiLayer
    {
        get => showPoiLayer;
        set
        {
            if (SetProperty(ref showPoiLayer, value))
            {
                RouteVisualizationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsOffRoute
    {
        get => isOffRoute;
        private set => SetProperty(ref isOffRoute, value);
    }

    public double OffRouteDistanceMeters
    {
        get => offRouteDistanceMeters;
        private set => SetProperty(ref offRouteDistanceMeters, value);
    }

    public string OffRouteStatusText
    {
        get => offRouteStatusText;
        private set => SetProperty(ref offRouteStatusText, value);
    }

    public string CurrentLocationText
    {
        get => currentLocationText;
        private set => SetProperty(ref currentLocationText, value);
    }

    public string NextManeuverInstruction
    {
        get => nextManeuverInstruction;
        private set => SetProperty(ref nextManeuverInstruction, value);
    }

    public string NextManeuverDistanceText
    {
        get => nextManeuverDistanceText;
        private set => SetProperty(ref nextManeuverDistanceText, value);
    }

    public string NextManeuverType
    {
        get => nextManeuverType;
        private set => SetProperty(ref nextManeuverType, value);
    }

    public string NextManeuverSymbol
    {
        get => nextManeuverSymbol;
        private set => SetProperty(ref nextManeuverSymbol, value);
    }

    public int NextManeuverIndex
    {
        get => nextManeuverIndex;
        private set => SetProperty(ref nextManeuverIndex, value);
    }

    public double NextManeuverProgress
    {
        get => nextManeuverProgress;
        private set => SetProperty(ref nextManeuverProgress, value);
    }

    public bool IsSpeakingPrompt
    {
        get => isSpeakingPrompt;
        private set => SetProperty(ref isSpeakingPrompt, value);
    }

    public string NextManeuverStepText => RouteManeuvers.Count == 0
        ? "Step 0/0"
        : $"Step {Math.Min(NextManeuverIndex, RouteManeuvers.Count)}/{RouteManeuvers.Count}";

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public string RouteCode
    {
        get => routeCode;
        private set => SetProperty(ref routeCode, value);
    }

    public string RouteSummary
    {
        get => routeSummary;
        private set => SetProperty(ref routeSummary, value);
    }

    public string RouteEngine
    {
        get => routeEngine;
        private set => SetProperty(ref routeEngine, value);
    }

    public string RouteSafety
    {
        get => routeSafety;
        private set => SetProperty(ref routeSafety, value);
    }

    public string RouteDiagnosticsSummary
    {
        get => routeDiagnosticsSummary;
        private set => SetProperty(ref routeDiagnosticsSummary, value);
    }

    public string RouteStartSnap
    {
        get => routeStartSnap;
        private set => SetProperty(ref routeStartSnap, value);
    }

    public string RouteEndSnap
    {
        get => routeEndSnap;
        private set => SetProperty(ref routeEndSnap, value);
    }

    public string RouteFailureReason
    {
        get => routeFailureReason;
        private set => SetProperty(ref routeFailureReason, value);
    }

    public string RouteEstimatedArrival
    {
        get => routeEstimatedArrival;
        private set => SetProperty(ref routeEstimatedArrival, value);
    }

    public GeoPoint? RouteStartPoint
    {
        get => routeStartPoint;
        private set => SetProperty(ref routeStartPoint, value);
    }

    public GeoPoint? RouteDestinationPoint
    {
        get => routeDestinationPoint;
        private set => SetProperty(ref routeDestinationPoint, value);
    }

    public GeoPoint? CurrentLocation
    {
        get => currentLocation;
        private set => SetProperty(ref currentLocation, value);
    }

    public async Task LoadInitialRouteAsync()
    {
        await RefreshRoutingModeAsync();
        await StartGpsTrackingAsync();

        if (CurrentLocation is not null && string.IsNullOrEmpty(StartLatitude))
        {
            await MainThread.InvokeOnMainThreadAsync(UseMyLocation);
        }
    }

    public void StopGpsTracking()
    {
        gpsTrackingCts?.Cancel();
        gpsTrackingCts = null;
    }

    private async Task StartGpsTrackingAsync()
    {
        if (gpsTrackingCts is not null)
        {
            return;
        }

        var permissionStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (permissionStatus != PermissionStatus.Granted)
        {
            permissionStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }

        if (permissionStatus != PermissionStatus.Granted)
        {
            OffRouteStatusText = "Pristup lokaciji je odbijen. Praćenje skretanja sa rute je isključeno.";
            await notifier.ShowWarningAsync(OffRouteStatusText).ConfigureAwait(false);
            return;
        }

        gpsTrackingCts = new CancellationTokenSource();
        _ = TrackGpsLoopAsync(gpsTrackingCts.Token);
    }

    private async Task TrackGpsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)),
                    cancellationToken);

                if (location is not null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() => UpdateCurrentLocation(location.Latitude, location.Longitude, location.Speed));
                }
            }
            catch (FeatureNotEnabledException)
            {
                await MainThread.InvokeOnMainThreadAsync(() => OffRouteStatusText = "Uključite lokaciju na uređaju za navođenje uživo.");
            }
            catch (Exception)
            {
                await MainThread.InvokeOnMainThreadAsync(() => OffRouteStatusText = "GPS lokacija trenutno nije dostupna.");
            }

            try
            {
                await Task.Delay(GpsPollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CalculateRouteAsync(GeoPoint? overrideStart = null, bool autoReroute = false)
    {
        ErrorMessage = null;

        if (overrideStart is null && string.IsNullOrEmpty(StartLatitude) && CurrentLocation is not null)
        {
            UseMyLocation();
        }

        if (string.IsNullOrEmpty(StartLatitude) || string.IsNullOrEmpty(DestinationLatitude))
        {
            ErrorMessage = "Izaberite polazak i odredište iz predloga.";
            return;
        }

        if (!TryParseCoordinate(StartLatitude, out var startLat) || !TryParseCoordinate(StartLongitude, out var startLon))
        {
            ErrorMessage = "Polazna tačka nije ispravna.";
            await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
            return;
        }

        if (!TryParseCoordinate(DestinationLatitude, out var destinationLat) || !TryParseCoordinate(DestinationLongitude, out var destinationLon))
        {
            ErrorMessage = "Odredište nije ispravno.";
            await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
            return;
        }

        if (!IsValidLatitude(startLat) || !IsValidLatitude(destinationLat) || !IsValidLongitude(startLon) || !IsValidLongitude(destinationLon))
        {
            ErrorMessage = "Coordinates are outside valid latitude/longitude ranges.";
            await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(Profile))
        {
            ErrorMessage = "Profil vozila je obavezan.";
            await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
            return;
        }

        var requestStart = overrideStart ?? new GeoPoint(startLat, startLon);
        var effectiveMode = ResolveEffectiveRoutingMode();

        if (string.Equals(effectiveMode, "offline", StringComparison.OrdinalIgnoreCase) && !OfflineRoutingAvailable)
        {
            ErrorMessage = "Izabran je offline režim, ali nijedan paket za rutiranje nije instaliran.";
            await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
            return;
        }

        try
        {
            await SetBusyAsync(true);
            await MainThread.InvokeOnMainThreadAsync(() =>
                StatusText = autoReroute
                    ? $"Automatsko preračunavanje rute ({effectiveMode})…"
                    : $"Izračunavanje rute ({effectiveMode})…");

            var response = await apiClient.GetRouteAsync(
                new RouteRequest(
                    requestStart,
                    new GeoPoint(destinationLat, destinationLon),
                    Profile.Trim(),
                    AvoidRestricted),
                CancellationToken.None).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() => ApplyRoute(response, requestStart.Lat, requestStart.Lon, destinationLat, destinationLon));
            await notifier.ShowSuccessAsync(autoReroute ? "Ruta je preračunata od trenutne lokacije." : "Ruta je izračunata.").ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = ex.Message;
                StatusText = "Zahtev za rutu nije uspeo.";
            });

            await notifier.ShowErrorAsync("Route request failed. Check network/API availability.").ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = ex.Message;
                StatusText = "Odgovor servisa za rute nije ispravan.";
            });

            await notifier.ShowErrorAsync("Odgovor servisa za rute nije ispravan.").ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false);
        }
    }

    private async Task RerouteFromCurrentLocationAsync()
    {
        if (CurrentLocation is null)
        {
            ErrorMessage = "Trenutna GPS lokacija nije dostupna za preračunavanje.";
            await notifier.ShowInfoAsync(ErrorMessage).ConfigureAwait(false);
            return;
        }

        await RefreshRoutingModeAsync();
        await CalculateRouteAsync(CurrentLocation);
    }

    private void ToggleRouteInput()
    {
        IsRouteInputVisible = !IsRouteInputVisible;
    }

    private void ApplyRoute(RouteResponse? route, double startLat, double startLon, double destinationLat, double destinationLon)
    {
        var selectedRoute = route?.Routes.Count > 0
            ? route.Routes[Math.Clamp(route.SelectedRouteIndex, 0, route.Routes.Count - 1)]
            : null;

        var diagnostics = route?.Diagnostics;
        var debug = selectedRoute?.Analysis?.Debug;
        var violations = selectedRoute?.Analysis?.Violations?.Count > 0
            ? selectedRoute.Analysis.Violations
            : route?.Violations ?? [];
        var highlights = debug?.Highlights?.Count > 0
            ? debug.Highlights
            : diagnostics?.Highlights ?? [];

        RouteCode = route?.Code ?? "NoResponse";

        var distanceMeters = selectedRoute?.Distance ?? route?.Summary?.DistanceMeters ?? 0;
        var durationSeconds = selectedRoute?.Duration ?? route?.Summary?.DurationSeconds ?? 0;

        RouteSummary = selectedRoute is null
            ? "Servis za rute nije vratio izabranu rutu."
            : $"{distanceMeters / 1000:0.#} km · {durationSeconds / 60:0} min";

        RouteEngine = diagnostics is null
            ? "Ruter: —"
            : diagnostics.UsedFallback
                ? $"Ruter: {diagnostics.Engine} · REZERVNI"
                : $"Ruter: {diagnostics.Engine}";

        RouteSafety = route is null
            ? "Provera za kamion u toku"
            : route.IsTruckSafe
                ? "Bezbedna za kamion"
                : violations.Count > 0
                    ? $"Upozorenja o ograničenjima: {violations.Count}"
                    : "Rutu treba proveriti";

        RouteEstimatedArrival = route?.Summary?.EstimatedArrival?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";

        var diagnosticsParts = new List<string>();
        if (diagnostics is not null && diagnostics.ExpandedStates > 0)
        {
            diagnosticsParts.Add($"{diagnostics.ExpandedStates} states");
        }

        var traversalCount = debug?.TraversalCount > 0 ? debug.TraversalCount : diagnostics?.TraversalCount ?? 0;
        if (traversalCount > 0)
        {
            diagnosticsParts.Add($"{traversalCount} traversals");
        }

        if (diagnostics?.GraphVersion is not null)
        {
            diagnosticsParts.Add($"graph {diagnostics.GraphVersion}");
        }

        if (diagnostics?.UsedFallback == true)
        {
            diagnosticsParts.Add("fallback active");
        }

        RouteDiagnosticsSummary = diagnosticsParts.Count > 0
            ? string.Join(" · ", diagnosticsParts)
            : debug?.Summary ?? "Nema dijagnostike.";

        RouteStartSnap = debug?.StartSnap ?? diagnostics?.StartSnap ?? "—";
        RouteEndSnap = debug?.EndSnap ?? diagnostics?.EndSnap ?? "—";
        RouteFailureReason = diagnostics?.FailureReason ?? "—";

        ReplaceItems(RouteHighlights, highlights);
        ReplaceItems(RouteManeuvers, route?.Maneuvers ?? []);
        ReplaceItems(RouteViolations, violations);
        RaisePropertyChanged(nameof(NextManeuverStepText));

        var routePoints = BuildRoutePoints(
            startLat,
            startLon,
            destinationLat,
            destinationLon,
            selectedRoute?.Geometry,
            route?.Maneuvers ?? []);
        ReplaceItems(RoutePolylinePoints, routePoints);

        RouteStartPoint = routePoints.Count > 0 ? routePoints[0] : new GeoPoint(startLat, startLon);
        RouteDestinationPoint = routePoints.Count > 0 ? routePoints[^1] : new GeoPoint(destinationLat, destinationLon);

        UpdateTurnByTurnCard();
        UpdateOffRouteState();

        HasRoute = selectedRoute is not null;
        RouteVisualizationChanged?.Invoke(this, EventArgs.Empty);
        StatusText = $"Ruta ažurirana u {DateTime.Now:HH:mm}";
    }

    private async Task RefreshRoutingModeAsync()
    {
        try
        {
            var hasBundle = await offlineRoutingBundleService.HasAnyBundleAsync(CancellationToken.None).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                OfflineRoutingAvailable = hasBundle;
                var effectiveMode = ResolveEffectiveRoutingMode();
                ActiveRoutingModeText = hasBundle
                    ? $"Režim rutiranja: {effectiveMode} (offline paket spreman)"
                    : $"Režim rutiranja: {effectiveMode} (offline paket nije instaliran)";
            });
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                OfflineRoutingAvailable = false;
                ActiveRoutingModeText = "Režim rutiranja: online (status offline paketa nije dostupan)";
            });
        }
    }

    private string ResolveEffectiveRoutingMode()
    {
        var mode = SelectedRoutingMode?.Trim();

        if (string.Equals(mode, "offline", StringComparison.OrdinalIgnoreCase))
        {
            return "offline";
        }

        if (string.Equals(mode, "online", StringComparison.OrdinalIgnoreCase))
        {
            return "online";
        }

        return OfflineRoutingAvailable ? "offline" : "online";
    }

    private Task SetBusyAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private void UpdateCurrentLocation(double latitude, double longitude, double? speedMps = null)
    {
        CurrentSpeedText = speedMps is > 0 ? (speedMps.Value * 3.6).ToString("0", CultureInfo.InvariantCulture) : "0";
        var latest = new GeoPoint(latitude, longitude);
        CurrentLocation = latest;
        CurrentLocationText = $"GPS: {latitude:0.00000}, {longitude:0.00000}";

        UpdateTurnByTurnCard();
        UpdateOffRouteState();
        RouteVisualizationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTurnByTurnCard()
    {
        if (RouteManeuvers.Count == 0)
        {
            NextManeuverInstruction = "Nema podataka o manevrima";
            NextManeuverDistanceText = "—";
            NextManeuverType = "Nastavite pravo";
            NextManeuverSymbol = "↑";
            NextManeuverIndex = 0;
            NextManeuverProgress = 0;
            UpdateLaneRibbon(NextManeuverSymbol);
            RaisePropertyChanged(nameof(NextManeuverStepText));
            return;
        }

        if (CurrentLocation is null)
        {
            var first = RouteManeuvers[0];
            NextManeuverInstruction = first.Instruction;
            NextManeuverDistanceText = "Čeka se GPS";
            NextManeuverType = ToManeuverLabel(first.Type);
            NextManeuverSymbol = ToManeuverSymbol(first.Type);
            NextManeuverIndex = 1;
            NextManeuverProgress = 0;
            maneuverInitialDistanceMeters = 1;
            UpdateLaneRibbon(NextManeuverSymbol);
            RaisePropertyChanged(nameof(NextManeuverStepText));
            return;
        }

        var closestIndex = 0;
        var closestDistance = double.MaxValue;

        for (var i = 0; i < RouteManeuvers.Count; i++)
        {
            var maneuver = RouteManeuvers[i];
            var distance = Location.CalculateDistance(
                CurrentLocation.Lat,
                CurrentLocation.Lon,
                maneuver.Latitude,
                maneuver.Longitude,
                DistanceUnits.Kilometers) * 1000;

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        var nextIndex = closestDistance <= ManeuverArrivalThresholdMeters && closestIndex + 1 < RouteManeuvers.Count
            ? closestIndex + 1
            : closestIndex;

        var nextManeuver = RouteManeuvers[nextIndex];
        var nextDistance = Location.CalculateDistance(
            CurrentLocation.Lat,
            CurrentLocation.Lon,
            nextManeuver.Latitude,
            nextManeuver.Longitude,
            DistanceUnits.Kilometers) * 1000;

        var maneuverChanged = NextManeuverIndex != nextIndex + 1;
        if (maneuverChanged)
        {
            maneuverInitialDistanceMeters = Math.Max(nextDistance, 1);
            lastSpokenDistanceBucket = -1;
        }

        NextManeuverInstruction = nextManeuver.Instruction;
        NextManeuverDistanceText = nextDistance < 1000
            ? $"Za {nextDistance:0} m"
            : $"Za {nextDistance / 1000:0.0} km";
        NextManeuverType = ToManeuverLabel(nextManeuver.Type);
        NextManeuverSymbol = ToManeuverSymbol(nextManeuver.Type);
        NextManeuverIndex = nextIndex + 1;
        NextManeuverProgress = Math.Clamp(1d - (nextDistance / Math.Max(maneuverInitialDistanceMeters, 1)), 0d, 1d);
        UpdateLaneRibbon(NextManeuverSymbol);
        RaisePropertyChanged(nameof(NextManeuverStepText));
        _ = SpeakNextPromptAsync();
    }

    private void UpdateOffRouteState()
    {
        if (CurrentLocation is null || RoutePolylinePoints.Count < 2)
        {
            OffRouteDistanceMeters = 0;
            IsOffRoute = false;
            OffRouteStatusText = "Off-route monitoring active";
            return;
        }

        var distanceMeters = CalculateDistanceToPolylineMeters(CurrentLocation, RoutePolylinePoints);
        OffRouteDistanceMeters = distanceMeters;
        IsOffRoute = distanceMeters > OffRouteThresholdMeters;
        OffRouteStatusText = IsOffRoute
            ? $"OFF ROUTE · {distanceMeters:0} m"
            : $"ON ROUTE · dev {distanceMeters:0} m";

        if (!IsOffRoute || !AutoRerouteEnabled || IsBusy || CurrentLocation is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - lastAutoRerouteAt < AutoRerouteCooldown)
        {
            return;
        }

        lastAutoRerouteAt = now;
        _ = CalculateRouteAsync(CurrentLocation, autoReroute: true);
    }

    private static double CalculateDistanceToPolylineMeters(GeoPoint point, IReadOnlyList<GeoPoint> path)
    {
        var minimum = double.MaxValue;

        for (var i = 0; i < path.Count - 1; i++)
        {
            var segmentDistance = DistancePointToSegmentMeters(point, path[i], path[i + 1]);
            if (segmentDistance < minimum)
            {
                minimum = segmentDistance;
            }
        }

        return minimum;
    }

    private static double DistancePointToSegmentMeters(GeoPoint point, GeoPoint start, GeoPoint end)
    {
        var latitudeFactor = 111320d;
        var longitudeFactor = 111320d * Math.Cos(((start.Lat + end.Lat + point.Lat) / 3d) * Math.PI / 180d);

        var px = point.Lon * longitudeFactor;
        var py = point.Lat * latitudeFactor;
        var x1 = start.Lon * longitudeFactor;
        var y1 = start.Lat * latitudeFactor;
        var x2 = end.Lon * longitudeFactor;
        var y2 = end.Lat * latitudeFactor;

        var dx = x2 - x1;
        var dy = y2 - y1;

        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        }

        var projection = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
        projection = Math.Clamp(projection, 0, 1);

        var closestX = x1 + projection * dx;
        var closestY = y1 + projection * dy;

        return Math.Sqrt((px - closestX) * (px - closestX) + (py - closestY) * (py - closestY));
    }

    private void UpdateLaneRibbon(string primarySymbol)
    {
        var lanes = primarySymbol switch
        {
            "←" or "↖" => new List<LaneCue>
            {
                new("←", "#10B981", "#1C5448", "#031712"),
                new("↑", "#10382F", "#1C5448", "#94A3B8"),
                new("↑", "#10382F", "#1C5448", "#94A3B8")
            },
            "→" or "↗" => new List<LaneCue>
            {
                new("↑", "#10382F", "#1C5448", "#94A3B8"),
                new("↑", "#10382F", "#1C5448", "#94A3B8"),
                new("→", "#10B981", "#1C5448", "#031712")
            },
            "⤵" => new List<LaneCue>
            {
                new("⤵", "#10B981", "#1C5448", "#031712"),
                new("↑", "#10382F", "#1C5448", "#94A3B8"),
                new("↑", "#10382F", "#1C5448", "#94A3B8")
            },
            _ => new List<LaneCue>
            {
                new("↑", "#10382F", "#1C5448", "#94A3B8"),
                new("↑", "#10B981", "#1C5448", "#031712"),
                new("↑", "#10382F", "#1C5448", "#94A3B8")
            }
        };

        ReplaceItems(LaneRibbon, lanes);
    }

    private async Task SpeakNextPromptAsync(bool force = false)
    {
        if (!VoiceGuidanceEnabled || IsSpeakingPrompt || RouteManeuvers.Count == 0 || NextManeuverIndex == 0)
        {
            return;
        }

        var currentIndex = Math.Clamp(NextManeuverIndex - 1, 0, RouteManeuvers.Count - 1);
        var bucket = ParseDistanceBucket(NextManeuverDistanceText);

        var shouldSpeak = force || lastSpokenManeuverIndex != currentIndex || (bucket >= 0 && bucket != lastSpokenDistanceBucket);
        if (!shouldSpeak)
        {
            return;
        }

        var prompt = bucket switch
        {
            >= 1000 => $"Za {bucket / 1000:0.#} kilometara, {NextManeuverInstruction}",
            > 0 => $"Za {bucket} metara, {NextManeuverInstruction}",
            _ => NextManeuverInstruction
        };

        try
        {
            IsSpeakingPrompt = true;
            serbianLocale ??= (await TextToSpeech.Default.GetLocalesAsync())
                .FirstOrDefault(locale => locale.Language.StartsWith("sr", StringComparison.OrdinalIgnoreCase));
            await TextToSpeech.Default.SpeakAsync(prompt, new SpeechOptions { Locale = serbianLocale });
            lastSpokenManeuverIndex = currentIndex;
            if (bucket >= 0)
            {
                lastSpokenDistanceBucket = bucket;
            }
        }
        catch
        {
            // Ignore speech device/transient errors and keep navigation running.
        }
        finally
        {
            IsSpeakingPrompt = false;
        }
    }

    private static int ParseDistanceBucket(string distanceText)
    {
        if (distanceText.StartsWith("Za ", StringComparison.OrdinalIgnoreCase))
        {
            var text = distanceText[3..].Trim();
            if (text.EndsWith(" m", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var meters))
            {
                return (int)Math.Round(meters / 50d) * 50;
            }

            if (text.EndsWith(" km", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(text[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var kilometers))
            {
                return (int)Math.Round(kilometers * 1000 / 250d) * 250;
            }
        }

        return -1;
    }

    private static string ToManeuverLabel(string? maneuverType)
    {
        return maneuverType?.Trim().ToLowerInvariant() switch
        {
            "left" or "turn-left" => "Skrenite levo",
            "right" or "turn-right" => "Skrenite desno",
            "slight-left" => "Blago levo",
            "slight-right" => "Blago desno",
            "sharp-left" => "Oštro levo",
            "sharp-right" => "Oštro desno",
            "uturn" or "u-turn" => "Polukružno okretanje",
            "arrive" or "destination" => "Stigli ste na odredište",
            "roundabout" => "Uđite u kružni tok",
            _ => "Nastavite pravo"
        };
    }

    private static string ToManeuverSymbol(string? maneuverType)
    {
        return maneuverType?.Trim().ToLowerInvariant() switch
        {
            "left" or "turn-left" => "←",
            "right" or "turn-right" => "→",
            "slight-left" => "↖",
            "slight-right" => "↗",
            "sharp-left" => "↙",
            "sharp-right" => "↘",
            "uturn" or "u-turn" => "⤵",
            "arrive" or "destination" => "◉",
            "roundabout" => "⟳",
            _ => "↑"
        };
    }

    private static bool TryParseCoordinate(string input, out double value)
        => double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           || double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool IsValidLatitude(double value) => value is >= -90 and <= 90;

    private static bool IsValidLongitude(double value) => value is >= -180 and <= 180;

    private static List<GeoPoint> BuildRoutePoints(
        double startLat,
        double startLon,
        double destinationLat,
        double destinationLon,
        JsonElement? geometry,
        IReadOnlyList<RouteManeuver> maneuvers)
    {
        var points = ExtractGeometryPoints(geometry);

        if (points.Count == 0)
        {
            points.Add(new GeoPoint(startLat, startLon));

            foreach (var maneuver in maneuvers)
            {
                points.Add(new GeoPoint(maneuver.Latitude, maneuver.Longitude));
            }

            points.Add(new GeoPoint(destinationLat, destinationLon));
        }

        return NormalizeRoutePoints(points);
    }

    private static List<GeoPoint> ExtractGeometryPoints(JsonElement? geometry)
    {
        if (geometry is null)
        {
            return [];
        }

        var root = geometry.Value;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (root.TryGetProperty("geometry", out var featureGeometry) && featureGeometry.ValueKind == JsonValueKind.Object)
        {
            root = featureGeometry;
        }

        if (!root.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<GeoPoint>(coordinates.GetArrayLength());

        foreach (var coordinate in coordinates.EnumerateArray())
        {
            if (coordinate.ValueKind != JsonValueKind.Array || coordinate.GetArrayLength() < 2)
            {
                continue;
            }

            if (!coordinate[1].TryGetDouble(out var lat) || !coordinate[0].TryGetDouble(out var lon))
            {
                continue;
            }

            if (!double.IsFinite(lat) || !double.IsFinite(lon))
            {
                continue;
            }

            result.Add(new GeoPoint(lat, lon));
        }

        return result;
    }

    private static List<GeoPoint> NormalizeRoutePoints(List<GeoPoint> points)
    {
        var normalized = new List<GeoPoint>(points.Count);
        GeoPoint? previous = null;

        foreach (var point in points)
        {
            if (previous is not null
                && Math.Abs(previous.Lat - point.Lat) < 0.000001
                && Math.Abs(previous.Lon - point.Lon) < 0.000001)
            {
                continue;
            }

            normalized.Add(point);
            previous = point;
        }

        return normalized;
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    public sealed record LaneCue(string Symbol, string FillColor, string StrokeColor, string TextColor);
}
