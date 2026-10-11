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
    private const string MyLocationLabel = "Moja lokacija";
    // A maneuver counts as passed once the truck is this far beyond it along the route.
    private const double ManeuverPassedToleranceMeters = 10;
    private static readonly TimeSpan GpsPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AutoRerouteCooldown = TimeSpan.FromSeconds(20);
    private readonly ApiClient apiClient;
    private readonly OfflineRoutingBundleService offlineRoutingBundleService;
    private readonly IMobileNotifier notifier;
    private readonly SavedRouteStore savedRouteStore;
    private readonly OfflineRoutingEngine offlineEngine;
    private CancellationTokenSource? gpsTrackingCts;
    private DateTimeOffset lastAutoRerouteAt = DateTimeOffset.MinValue;
    // Auto-reroute only after the truck has been on the route once: a route planned
    // from somewhere else (start typed in) must not be replaced from the GPS position.
    private bool hasBeenOnRoute;
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
    private string offRouteStatusText = "Praćenje skretanja sa rute nije aktivno";
    private string nextManeuverInstruction = "Izračunajte rutu za početak navođenja";
    private string nextManeuverDistanceText = "—";
    private string nextManeuverType = "Nastavite pravo";
    private string nextManeuverKind = "continue";
    private string nextManeuverDistanceShort = "—";
    private string nextManeuverRoad = "Izračunajte rutu za početak navođenja";
    private string thenManeuverKind = "";
    private bool hasThenManeuver;
    private string remainingDistanceText = "—";
    private string remainingTimeText = "—";
    private string arrivalTimeText = "—";
    private double routeTotalDistanceMeters;
    private double routeTotalDurationSeconds;
    private string nextManeuverSymbol = "↑";
    private GeoPoint? routeStartPoint;
    private GeoPoint? routeDestinationPoint;
    private GeoPoint? currentLocation;
    private string startQuery = string.Empty;
    private string destinationQuery = string.Empty;
    private string activeField = "destination";
    // Field the shown suggestions were searched for (a tap must fill that field, not
    // whichever was edited last) and the name that belongs to the destination
    // coordinates (saved with the route and the recent destinations).
    private string suggestionsField = "destination";
    private string? destinationLabel;
    private bool suppressSearch;
    private CancellationTokenSource? searchCts;
    private string currentSpeedText = "0";
    private bool hasRoute;

    public NavigationViewModel(ApiClient apiClient, OfflineRoutingBundleService offlineRoutingBundleService, IMobileNotifier notifier, SavedRouteStore savedRouteStore, OfflineRoutingEngine offlineEngine)
    {
        this.offlineEngine = offlineEngine;
        this.apiClient = apiClient;
        this.offlineRoutingBundleService = offlineRoutingBundleService;
        this.notifier = notifier;
        this.savedRouteStore = savedRouteStore;

        RouteHighlights = [];
        RouteManeuvers = [];
        RouteViolations = [];
        RoutePolylinePoints = [];
        LaneRibbon = [];

        CalculateRouteCommand = new Command(async () => await CalculateRouteAsync(), () => !IsBusy);
        ToggleRouteInputCommand = new Command(ToggleRouteInput);
        RerouteCommand = new Command(async () => await RerouteFromCurrentLocationAsync(), () => !IsBusy);
        SpeakNextPromptCommand = new Command(async () => await SpeakNextPromptAsync(force: true));
        ToggleSimulationCommand = new Command(ToggleSimulation);

        RoutingModes = ["Auto", "Online", "Offline"];

        Suggestions = [];
        SelectSuggestionCommand = new Command<GeocodeResult>(SelectSuggestion);
        UseMyLocationCommand = new Command(async () => await UseMyLocationAsync());
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
            destinationLabel = null;
            activeField = "destination";
            _ = SearchAsync(value);
        }
    }

    private async Task SearchAsync(string query)
    {
        searchCts?.Cancel();
        var cts = searchCts = new CancellationTokenSource();
        var field = activeField;

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            ReplaceItems(Suggestions, []);
            RaisePropertyChanged(nameof(HasSuggestions));
            return;
        }

        try
        {
            await Task.Delay(350, cts.Token);
            var results = await GeocodeWithFallbackAsync(query.Trim(), cts.Token);
            if (cts.IsCancellationRequested) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                suggestionsField = field;
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
        if (suggestionsField == "start")
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
            destinationLabel = item.Title;
        }
        suppressSearch = false;
        ReplaceItems(Suggestions, []);
        RaisePropertyChanged(nameof(HasSuggestions));
    }

    private async Task UseMyLocationAsync()
    {
        if (!await EnsureCurrentLocationAsync(showWarnings: true).ConfigureAwait(false) || CurrentLocation is null)
        {
            ErrorMessage = "GPS lokacija još nije dostupna.";
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            suppressSearch = true;
            StartQuery = MyLocationLabel;
            StartLatitude = CurrentLocation.Lat.ToString("0.000000", CultureInfo.InvariantCulture);
            StartLongitude = CurrentLocation.Lon.ToString("0.000000", CultureInfo.InvariantCulture);
            suppressSearch = false;
            ReplaceItems(Suggestions, []);
            RaisePropertyChanged(nameof(HasSuggestions));
        });
    }

    private void SwapPoints()
    {
        suppressSearch = true;
        (StartQuery, DestinationQuery) = (DestinationQuery, StartQuery);
        (StartLatitude, DestinationLatitude) = (DestinationLatitude, StartLatitude);
        (StartLongitude, DestinationLongitude) = (DestinationLongitude, StartLongitude);
        destinationLabel = string.IsNullOrEmpty(DestinationLatitude) ? null : DestinationQuery;
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

    public ICommand ToggleSimulationCommand { get; }

    // ---- Drive simulation (testing without driving) ----
    // Feeds positions along the calculated route once per second, like a GPS fix;
    // real GPS fixes are ignored while it runs.
    private const double SimulationSpeedMps = 50 / 3.6;
    private CancellationTokenSource? simulationCts;

    public bool IsSimulating => simulationCts is not null;

    public string SimulationButtonText => IsSimulating ? "Zaustavi simulaciju" : "Simuliraj vožnju";

    private void ToggleSimulation()
    {
        if (simulationCts is { } running)
        {
            running.Cancel();
            simulationCts = null;
            RaiseSimulationChanged();
            return;
        }

        if (RoutePolylinePoints.Count < 2)
        {
            _ = notifier.ShowInfoAsync("Prvo izračunajte rutu, pa pokrenite simulaciju.");
            return;
        }

        var cts = new CancellationTokenSource();
        simulationCts = cts;
        RaiseSimulationChanged();
        _ = RunSimulationAsync(RoutePolylinePoints.ToList(), cts);
    }

    private void RaiseSimulationChanged()
    {
        RaisePropertyChanged(nameof(IsSimulating));
        RaisePropertyChanged(nameof(SimulationButtonText));
    }

    private async Task RunSimulationAsync(IReadOnlyList<GeoPoint> route, CancellationTokenSource cts)
    {
        try
        {
            var segment = 0;
            var intoSegment = 0.0;

            while (!cts.IsCancellationRequested && segment < route.Count - 1)
            {
                var from = route[segment];
                var to = route[segment + 1];
                var length = DistanceMeters(from, to);
                var t = length > 0 ? Math.Min(1, intoSegment / length) : 1;
                var lat = from.Lat + (to.Lat - from.Lat) * t;
                var lon = from.Lon + (to.Lon - from.Lon) * t;

                await MainThread.InvokeOnMainThreadAsync(() => UpdateCurrentLocation(lat, lon, SimulationSpeedMps));
                await Task.Delay(1000, cts.Token);

                // Advance one second of driving, across as many route points as needed.
                intoSegment += SimulationSpeedMps;
                while (segment < route.Count - 1)
                {
                    var segmentLength = DistanceMeters(route[segment], route[segment + 1]);
                    if (intoSegment <= segmentLength)
                    {
                        break;
                    }

                    intoSegment -= segmentLength;
                    segment++;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(simulationCts, cts))
            {
                simulationCts = null;
                await MainThread.InvokeOnMainThreadAsync(RaiseSimulationChanged);
            }

            cts.Dispose();
        }
    }

    private static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        const double earthRadius = 6371000;
        var dLat = (b.Lat - a.Lat) * Math.PI / 180;
        var dLon = (b.Lon - a.Lon) * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(a.Lat * Math.PI / 180) * Math.Cos(b.Lat * Math.PI / 180) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

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

    /// <summary>
    /// Runs a request from the home screen: open the planner, or route from the
    /// current position to a recent destination.
    /// </summary>
    public async Task ApplyRequestAsync(HomeNavigationRequest request)
    {
        if (request.Destination is { } destination)
        {
            suppressSearch = true;
            DestinationQuery = destination.Title;
            destinationLabel = destination.Title;
            DestinationLatitude = destination.Latitude.ToString("0.000000", CultureInfo.InvariantCulture);
            DestinationLongitude = destination.Longitude.ToString("0.000000", CultureInfo.InvariantCulture);
            StartQuery = MyLocationLabel;
            StartLatitude = string.Empty;
            StartLongitude = string.Empty;
            suppressSearch = false;

            await CalculateRouteAsync();

            // No route (e.g. no GPS fix indoors): show the planner with the destination
            // filled in and the error, so the driver can type a start instead.
            if (RoutePolylinePoints.Count == 0)
            {
                await MainThread.InvokeOnMainThreadAsync(() => IsRouteInputVisible = true);
            }

            return;
        }

        if (request.OpenPlanner)
        {
            IsRouteInputVisible = true;
        }
    }

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

    // ---- TomTom-style guidance banner ----

    /// <summary>Raw maneuver type (left, slight-right, roundabout, arrive…) used to pick the banner icon.</summary>
    public string NextManeuverKind
    {
        get => nextManeuverKind;
        private set => SetProperty(ref nextManeuverKind, value);
    }

    /// <summary>Distance to the next maneuver without the "Za" prefix, e.g. "350 m" or "1,2 km".</summary>
    public string NextManeuverDistanceShort
    {
        get => nextManeuverDistanceShort;
        private set => SetProperty(ref nextManeuverDistanceShort, value);
    }

    /// <summary>Road the next maneuver leads onto; falls back to the instruction text.</summary>
    public string NextManeuverRoad
    {
        get => nextManeuverRoad;
        private set => SetProperty(ref nextManeuverRoad, value);
    }

    /// <summary>The maneuver after the next one, shown small as "zatim".</summary>
    public string ThenManeuverKind
    {
        get => thenManeuverKind;
        private set => SetProperty(ref thenManeuverKind, value);
    }

    public bool HasThenManeuver
    {
        get => hasThenManeuver;
        private set => SetProperty(ref hasThenManeuver, value);
    }

    public string RemainingDistanceText
    {
        get => remainingDistanceText;
        private set => SetProperty(ref remainingDistanceText, value);
    }

    public string RemainingTimeText
    {
        get => remainingTimeText;
        private set => SetProperty(ref remainingTimeText, value);
    }

    public string ArrivalTimeText
    {
        get => arrivalTimeText;
        private set => SetProperty(ref arrivalTimeText, value);
    }

    private void UpdateGuidanceBanner(int nextIndex, double? nextDistanceMeters)
    {
        var next = RouteManeuvers[nextIndex];
        NextManeuverKind = (next.Type ?? "").Trim().ToLowerInvariant();
        NextManeuverRoad = !string.IsNullOrWhiteSpace(next.RoadName)
            ? next.RoadName!
            : !string.IsNullOrWhiteSpace(next.RoadRef) ? next.RoadRef! : next.Instruction;
        NextManeuverDistanceShort = nextDistanceMeters is null
            ? "—"
            : FormatDistance(nextDistanceMeters.Value);

        var hasThen = nextIndex + 1 < RouteManeuvers.Count;
        HasThenManeuver = hasThen;
        ThenManeuverKind = hasThen ? (RouteManeuvers[nextIndex + 1].Type ?? "").Trim().ToLowerInvariant() : "";

        // Remaining distance = what is left after the next maneuver + the way to it.
        if (routeTotalDistanceMeters > 0)
        {
            var remaining = Math.Max(0, routeTotalDistanceMeters - next.DistanceFromRouteStartMeters)
                + (nextDistanceMeters ?? 0);
            remaining = Math.Min(remaining, routeTotalDistanceMeters);
            var seconds = routeTotalDurationSeconds * remaining / routeTotalDistanceMeters;
            RemainingDistanceText = FormatDistance(remaining);
            RemainingTimeText = FormatDuration(seconds);
            ArrivalTimeText = DateTime.Now.AddSeconds(seconds).ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }

    private static string FormatDistance(double meters) => meters switch
    {
        < 50 => $"{Math.Round(meters / 5) * 5:0} m",
        < 1000 => $"{Math.Round(meters / 10) * 10:0} m",
        < 10_000 => $"{meters / 1000:0.0} km",
        _ => $"{meters / 1000:0} km"
    };

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{Math.Max(1, time.Minutes)} min";
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
        // Load the offline package in the background, so the first search or route
        // without signal does not wait for it (reading it takes a few seconds).
        _ = Task.Run(() => offlineEngine.EnsureLoadedAsync(CurrentLocation));

        // Guidance survives an app restart without signal: bring back the last route.
        if (!HasRoute && savedRouteStore.Load() is { } saved)
        {
            await MainThread.InvokeOnMainThreadAsync(() => RestoreSavedRoute(saved));
        }

        await RefreshRoutingModeAsync();
        await StartGpsTrackingAsync();

        if (CurrentLocation is null)
        {
            await EnsureCurrentLocationAsync(showWarnings: false).ConfigureAwait(false);
        }

        if (CurrentLocation is not null && string.IsNullOrEmpty(StartLatitude))
        {
            await UseMyLocationAsync().ConfigureAwait(false);
        }
    }

    public void StopGpsTracking()
    {
        simulationCts?.Cancel();
        simulationCts = null;
        RaiseSimulationChanged();

        gpsTrackingCts?.Cancel();
        gpsTrackingCts = null;

        if (isGpsListening)
        {
            Geolocation.Default.LocationChanged -= OnGpsLocationChanged;
            Geolocation.Default.StopListeningForeground();
            isGpsListening = false;
        }
    }

    /// <summary>
    /// Ensures that a usable current GPS fix is available for mobile navigation actions.
    /// </summary>
    public async Task<bool> EnsureCurrentLocationAsync(bool showWarnings)
    {
        if (CurrentLocation is not null)
        {
            return true;
        }

        var permissionStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>().ConfigureAwait(false);
        if (permissionStatus != PermissionStatus.Granted)
        {
            // The permission prompt must be shown from the main thread (after the
            // ConfigureAwait(false) above this runs on a background thread).
            permissionStatus = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.LocationWhenInUse>);
        }

        if (permissionStatus != PermissionStatus.Granted)
        {
            const string deniedMessage = "Pristup lokaciji je odbijen. Uključite dozvolu za lokaciju.";
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = deniedMessage;
                OffRouteStatusText = "Pristup lokaciji je odbijen. Praćenje skretanja sa rute je isključeno.";
            });

            if (showWarnings)
            {
                await notifier.ShowWarningAsync(deniedMessage).ConfigureAwait(false);
            }

            return false;
        }

        try
        {
            var lastKnown = await Geolocation.Default.GetLastKnownLocationAsync().ConfigureAwait(false);
            if (lastKnown is not null)
            {
                await MainThread.InvokeOnMainThreadAsync(() => UpdateCurrentLocation(lastKnown.Latitude, lastKnown.Longitude, lastKnown.Speed));
                return true;
            }

            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10))).ConfigureAwait(false);

            if (location is not null)
            {
                await MainThread.InvokeOnMainThreadAsync(() => UpdateCurrentLocation(location.Latitude, location.Longitude, location.Speed));
                return true;
            }
        }
        catch (FeatureNotEnabledException)
        {
            ErrorMessage = "Uključite lokaciju na uređaju za navigaciju uživo.";
            OffRouteStatusText = ErrorMessage;

            if (showWarnings)
            {
                await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
            }

            return false;
        }
        catch (Exception)
        {
            // Continuous listening / polling remains the fallback; this method only tries
            // to get a usable fix faster for buttons that act immediately.
        }

        ErrorMessage = "GPS lokacija trenutno nije dostupna.";
        if (showWarnings)
        {
            await notifier.ShowInfoAsync(ErrorMessage).ConfigureAwait(false);
        }

        return CurrentLocation is not null;
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

        // Continuous updates (~1 s) keep the truck moving smoothly; polling every
        // few seconds made it jump. Polling stays as the fallback.
        try
        {
            Geolocation.Default.LocationChanged += OnGpsLocationChanged;
            isGpsListening = await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1)));
        }
        catch (Exception)
        {
            isGpsListening = false;
        }

        if (!isGpsListening)
        {
            Geolocation.Default.LocationChanged -= OnGpsLocationChanged;
            _ = TrackGpsLoopAsync(gpsTrackingCts.Token);
        }
    }

    private bool isGpsListening;

    private void OnGpsLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        var location = e.Location;
        if (IsSimulating)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => UpdateCurrentLocation(location.Latitude, location.Longitude, location.Speed));
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

                if (location is not null && !IsSimulating)
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

    /// <summary>
    /// Fills in route points the driver typed but did not pick from the suggestions
    /// (TomTom-style): an empty start or "Moja lokacija" means the current GPS fix,
    /// any other text is geocoded and the best match is used.
    /// </summary>
    private async Task<bool> ResolveMissingPointsAsync()
    {
        if (string.IsNullOrEmpty(StartLatitude))
        {
            var query = StartQuery?.Trim() ?? "";
            if (query.Length == 0 || string.Equals(query, MyLocationLabel, StringComparison.OrdinalIgnoreCase))
            {
                if (CurrentLocation is null)
                {
                    try
                    {
                        var fix = await Geolocation.Default.GetLocationAsync(
                            new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
                        if (fix is not null)
                        {
                            CurrentLocation = new GeoPoint(fix.Latitude, fix.Longitude);
                        }
                    }
                    catch
                    {
                        // Permission denied or GPS off: reported below.
                    }
                }

                if (CurrentLocation is null)
                {
                    ErrorMessage = "GPS lokacija još nije dostupna. Uključite lokaciju ili unesite polaznu adresu.";
                    return false;
                }

                await UseMyLocationAsync().ConfigureAwait(false);
            }
            else if (await GeocodeFirstAsync(query) is { } start)
            {
                await MainThread.InvokeOnMainThreadAsync(() => SetPoint("start", start));
            }
            else
            {
                ErrorMessage = $"Polazna adresa „{query}“ nije pronađena.";
                return false;
            }
        }

        if (string.IsNullOrEmpty(DestinationLatitude))
        {
            var query = DestinationQuery?.Trim() ?? "";
            if (query.Length == 0)
            {
                ErrorMessage = "Unesite odredište.";
                return false;
            }

            if (await GeocodeFirstAsync(query) is { } destination)
            {
                await MainThread.InvokeOnMainThreadAsync(() => SetPoint("destination", destination));
            }
            else
            {
                ErrorMessage = $"Odredište „{query}“ nije pronađeno.";
                return false;
            }
        }

        return true;
    }

    private async Task<GeocodeResult?> GeocodeFirstAsync(string query)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var results = await GeocodeWithFallbackAsync(query, cts.Token);
            return results.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Server geocoder first; without a connection (or no answer) the place index of
    /// the installed offline package on the phone.
    /// </summary>
    private async Task<List<GeocodeResult>> GeocodeWithFallbackAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            var online = RankSerbiaFirst(await apiClient.GeocodeAsync(query, timeout.Token));
            if (online.Count > 0)
            {
                return online;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // No connection: fall through to the phone.
        }

        cancellationToken.ThrowIfCancellationRequested();
        var offline = await offlineEngine.SearchAsync(query, CurrentLocation, 8, cancellationToken);
        return offline.ToList();
    }

    /// <summary>
    /// Route from the server (it also knows turn restrictions and live closures); when
    /// the server cannot be reached, or offline mode is chosen, from the offline package
    /// on the phone. Throws like the API call when neither works.
    /// </summary>
    private async Task<RouteResponse?> GetRouteWithFallbackAsync(RouteRequest request)
    {
        var offlineOnly = string.Equals(SelectedRoutingMode?.Trim(), "offline", StringComparison.OrdinalIgnoreCase);

        // With a package installed, do not wait for a server that does not answer.
        if (!offlineOnly &&
            await offlineEngine.EnsureLoadedAsync(request.Start).ConfigureAwait(false) &&
            !await apiClient.IsServerReachableAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false))
        {
            offlineOnly = true;
        }

        if (!offlineOnly)
        {
            try
            {
                var online = await apiClient.GetRouteAsync(request, CancellationToken.None).ConfigureAwait(false);

                // The server only covers its own region (Serbia and the Western Balkans): a route that does not
                // reach the requested points (Belgrade-Budapest ending at the border) is not
                // a route. Use an installed offline region that covers both ends instead.
                if (!ReachesEnds(online, request))
                {
                    if (await offlineEngine.RouteAsync(request.Start, request.Destination).ConfigureAwait(false) is { } covering)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() => ActiveRoutingModeText = "Režim rutiranja: telefon (offline region)");
                        return covering;
                    }

                    throw new InvalidOperationException(
                        "Ruta izlazi iz oblasti koju server pokriva. Preuzmite region koji obuhvata celu rutu " +
                        "(Offline mape → Paketi za rutiranje, npr. Zapadni Balkan).");
                }

                return online;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                // Includes the server's "OutOfCoverage" answer for points outside its region.
                if (!await offlineEngine.EnsureLoadedAsync(request.Start).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }

        var offline = await offlineEngine.RouteAsync(request.Start, request.Destination).ConfigureAwait(false);
        if (offline is null)
        {
            throw new InvalidOperationException(offlineEngine.LoadProblem
                ?? (await offlineEngine.EnsureLoadedAsync(request.Start).ConfigureAwait(false)
                    ? "Offline ruta nije pronađena (tačka je predaleko od puta za kamione ili su delovi rute povezani samo trajektom koji nije u paketu)."
                    : "Offline paket za rutiranje nije instaliran (Offline mape → Paketi za rutiranje)."));
        }

        await MainThread.InvokeOnMainThreadAsync(() => ActiveRoutingModeText = "Režim rutiranja: telefon (offline)");
        return offline;
    }

    /// <summary>True when the route starts and ends within 2 km of the requested points.</summary>
    private static bool ReachesEnds(RouteResponse? response, RouteRequest request)
    {
        var route = response?.Routes.Count > 0
            ? response.Routes[Math.Clamp(response.SelectedRouteIndex, 0, response.Routes.Count - 1)]
            : null;
        var points = ExtractGeometryPoints(route?.Geometry);
        if (points.Count < 2)
        {
            return true; // nothing to judge by; keep the server's answer
        }

        static double Gap(GeoPoint a, GeoPoint b)
        {
            var lat = (a.Lat + b.Lat) / 2 * Math.PI / 180;
            var dx = (b.Lon - a.Lon) * 111_320 * Math.Cos(lat);
            var dy = (b.Lat - a.Lat) * 110_540;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        return Gap(points[0], request.Start) <= 2000 && Gap(points[^1], request.Destination) <= 2000;
    }

    // The fleet drives in Serbia: "Nis" must mean Niš before Nice. Keeps the geocoder's
    // order within each group (stable sort).
    private static List<GeocodeResult> RankSerbiaFirst(IEnumerable<GeocodeResult> results) =>
        results
            .OrderByDescending(result =>
                result.DisplayName.Contains("Србија", StringComparison.OrdinalIgnoreCase) ||
                result.DisplayName.Contains("Srbija", StringComparison.OrdinalIgnoreCase) ||
                result.DisplayName.Contains("Serbia", StringComparison.OrdinalIgnoreCase))
            .ToList();

    // Sets a resolved point without triggering a new search (keeps the typed text).
    private void SetPoint(string field, GeocodeResult item)
    {
        suppressSearch = true;
        var lat = item.Lat.ToString("0.000000", CultureInfo.InvariantCulture);
        var lon = item.Lon.ToString("0.000000", CultureInfo.InvariantCulture);
        if (field == "start")
        {
            StartLatitude = lat;
            StartLongitude = lon;
        }
        else
        {
            DestinationLatitude = lat;
            DestinationLongitude = lon;
            destinationLabel = string.IsNullOrWhiteSpace(DestinationQuery) ? item.Title : DestinationQuery;
        }
        suppressSearch = false;
        ReplaceItems(Suggestions, []);
        RaisePropertyChanged(nameof(HasSuggestions));
    }

    private async Task CalculateRouteAsync(GeoPoint? overrideStart = null, bool autoReroute = false)
    {
        ErrorMessage = null;

        if (overrideStart is null && !await ResolveMissingPointsAsync())
        {
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
            ErrorMessage = "Koordinate su van dozvoljenog opsega.";
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

            var response = await GetRouteWithFallbackAsync(
                new RouteRequest(
                    requestStart,
                    new GeoPoint(destinationLat, destinationLon),
                    Profile.Trim(),
                    AvoidRestricted)).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyRoute(response, requestStart.Lat, requestStart.Lon, destinationLat, destinationLon);
                // Like TomTom: once the route is ready the planner gets out of the way.
                IsRouteInputVisible = false;
            });

            if (response?.Routes.Count > 0)
            {
                savedRouteStore.Save(new SavedRoute(
                    response,
                    requestStart.Lat,
                    requestStart.Lon,
                    destinationLat,
                    destinationLon,
                    destinationLabel ?? (string.IsNullOrWhiteSpace(DestinationQuery) ? "Odredište" : DestinationQuery),
                    DateTime.UtcNow));
            }

            if (!autoReroute)
            {
                RecentDestinations.Add(destinationLabel ?? DestinationQuery, destinationLat, destinationLon);
            }
            await notifier.ShowSuccessAsync(autoReroute ? "Ruta je preračunata od trenutne lokacije." : "Ruta je izračunata.").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // No signal / server unreachable. The current (saved) route keeps guiding;
            // a new route needs the server until on-device routing exists.
            const string offlineMessage = "Nema veze sa serverom. Nova ruta može da se izračuna kad se veza vrati.";
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusText = autoReroute
                    ? "Van rute — preračunavanje čim se veza vrati."
                    : "Zahtev za rutu nije uspeo.";
                if (!autoReroute)
                {
                    ErrorMessage = HasRoute ? offlineMessage : $"{offlineMessage} ({ex.Message})";
                }
            });

            if (!autoReroute)
            {
                await notifier.ShowErrorAsync(offlineMessage).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = ex.Message;
                StatusText = "Ruta nije izračunata.";
            });

            await notifier.ShowErrorAsync(ex.Message).ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false);
        }
    }

    private void RestoreSavedRoute(SavedRoute saved)
    {
        suppressSearch = true;
        DestinationQuery = saved.DestinationTitle;
        destinationLabel = saved.DestinationTitle;
        DestinationLatitude = saved.DestinationLat.ToString("0.000000", CultureInfo.InvariantCulture);
        DestinationLongitude = saved.DestinationLon.ToString("0.000000", CultureInfo.InvariantCulture);
        StartLatitude = saved.StartLat.ToString("0.000000", CultureInfo.InvariantCulture);
        StartLongitude = saved.StartLon.ToString("0.000000", CultureInfo.InvariantCulture);
        suppressSearch = false;

        ApplyRoute(saved.Response, saved.StartLat, saved.StartLon, saved.DestinationLat, saved.DestinationLon);
        StatusText = $"Sačuvana ruta ({saved.SavedAtUtc.ToLocalTime():dd.MM. HH:mm}) — radi i bez signala.";
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
        hasBeenOnRoute = false;
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
        routeTotalDistanceMeters = distanceMeters;
        routeTotalDurationSeconds = durationSeconds;
        RemainingDistanceText = distanceMeters > 0 ? FormatDistance(distanceMeters) : "—";
        RemainingTimeText = durationSeconds > 0 ? FormatDuration(durationSeconds) : "—";
        ArrivalTimeText = durationSeconds > 0
            ? DateTime.Now.AddSeconds(durationSeconds).ToString("HH:mm", CultureInfo.InvariantCulture)
            : "—";

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

        // Auto: server first, the phone when the server cannot be reached.
        return OfflineRoutingAvailable ? "online + offline rezerva" : "online";
    }

    private double[] routeCumulativeMeters = [];
    private int routeCumulativeForCount = -1;

    /// <summary>
    /// Distance travelled along the route: the cumulative length up to the route
    /// point nearest the truck. Null when the truck is more than 60 m off the route.
    /// </summary>
    private double? RouteProgressMeters(GeoPoint position)
    {
        var points = RoutePolylinePoints;
        if (points.Count < 2)
        {
            return null;
        }

        if (routeCumulativeForCount != points.Count)
        {
            routeCumulativeMeters = new double[points.Count];
            for (var i = 1; i < points.Count; i++)
            {
                routeCumulativeMeters[i] = routeCumulativeMeters[i - 1] + Location.CalculateDistance(
                    points[i - 1].Lat, points[i - 1].Lon, points[i].Lat, points[i].Lon, DistanceUnits.Kilometers) * 1000;
            }
            routeCumulativeForCount = points.Count;
        }

        var nearest = 0;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < points.Count; i++)
        {
            var d = Location.CalculateDistance(position.Lat, position.Lon, points[i].Lat, points[i].Lon, DistanceUnits.Kilometers) * 1000;
            if (d < nearestDistance)
            {
                nearestDistance = d;
                nearest = i;
            }
        }

        return nearestDistance <= 60 ? routeCumulativeMeters[nearest] : null;
    }

    private double smoothedSpeedKph;
    private GeoPoint? speedSamplePoint;
    private DateTimeOffset speedSampleAt;

    /// <summary>
    /// GPS speed when the fix carries one; otherwise derived from the distance and
    /// time since the previous fix (emulators and some devices report no speed).
    /// Implausible jumps (above 160 km/h for a truck) are ignored.
    /// </summary>
    private double ResolveSpeedKph(double latitude, double longitude, double? speedMps)
    {
        var now = DateTimeOffset.UtcNow;
        var previous = speedSamplePoint;
        var previousAt = speedSampleAt;
        speedSamplePoint = new GeoPoint(latitude, longitude);
        speedSampleAt = now;

        if (speedMps is > 0)
        {
            return speedMps.Value * 3.6;
        }

        if (previous is null)
        {
            return 0;
        }

        var seconds = (now - previousAt).TotalSeconds;
        if (seconds < 0.5)
        {
            return -1; // too close to measure: keep the current speed
        }

        var meters = Location.CalculateDistance(previous.Lat, previous.Lon, latitude, longitude, DistanceUnits.Kilometers) * 1000;
        var kph = meters / seconds * 3.6;
        return kph is > 1 and <= 160 ? kph : 0;
    }

    private Task SetBusyAsync(bool value)
        => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private void UpdateCurrentLocation(double latitude, double longitude, double? speedMps = null)
    {
        // Exponential smoothing: fixes arriving close together (or late) made the
        // derived speed jump, e.g. 54 -> 122 km/h.
        var rawKph = ResolveSpeedKph(latitude, longitude, speedMps);
        smoothedSpeedKph = rawKph < 0
            ? smoothedSpeedKph
            : rawKph == 0 ? smoothedSpeedKph * 0.5 : smoothedSpeedKph * 0.7 + rawKph * 0.3;
        if (smoothedSpeedKph < 1) smoothedSpeedKph = 0;
        CurrentSpeedText = smoothedSpeedKph.ToString("0", CultureInfo.InvariantCulture);
        var latest = new GeoPoint(latitude, longitude);
        CurrentLocation = latest;
        CurrentLocationText = $"GPS: {latitude:0.00000}, {longitude:0.00000}";

        UpdateTurnByTurnCard();
        UpdateOffRouteState();
        // No RouteVisualizationChanged here: the CurrentLocation change already pushes
        // the position to the map; raising it too sent every fix twice (2 ms apart),
        // which halved the measured fix interval and made the truck stall.
    }

    private void UpdateTurnByTurnCard()
    {
        if (RouteManeuvers.Count == 0)
        {
            NextManeuverInstruction = "Nema podataka o manevrima";
            NextManeuverDistanceText = "—";
            NextManeuverType = "Nastavite pravo";
            NextManeuverSymbol = "↑";
            NextManeuverKind = "continue";
            NextManeuverDistanceShort = "—";
            NextManeuverRoad = "Izračunajte rutu za početak navođenja";
            HasThenManeuver = false;
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
            UpdateGuidanceBanner(RouteManeuvers.Count > 1 ? 1 : 0, null);
            NextManeuverIndex = 1;
            NextManeuverProgress = 0;
            maneuverInitialDistanceMeters = 1;
            UpdateLaneRibbon(NextManeuverSymbol);
            RaisePropertyChanged(nameof(NextManeuverStepText));
            return;
        }

        // TomTom-style: the next maneuver is the first one AHEAD of the truck along
        // the route, not the nearest one (the nearest is often the turn just passed).
        int nextIndex;
        double nextDistance;
        var progress = RouteProgressMeters(CurrentLocation);

        if (progress is { } travelled)
        {
            nextIndex = RouteManeuvers.Count - 1;
            for (var i = 0; i < RouteManeuvers.Count; i++)
            {
                if (RouteManeuvers[i].DistanceFromRouteStartMeters > travelled + ManeuverPassedToleranceMeters)
                {
                    nextIndex = i;
                    break;
                }
            }

            nextDistance = Math.Max(0, RouteManeuvers[nextIndex].DistanceFromRouteStartMeters - travelled);
        }
        else
        {
            // Off the route: fall back to the nearest maneuver in a straight line.
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

            nextIndex = closestDistance <= ManeuverArrivalThresholdMeters && closestIndex + 1 < RouteManeuvers.Count
                ? closestIndex + 1
                : closestIndex;
            nextDistance = Location.CalculateDistance(
                CurrentLocation.Lat,
                CurrentLocation.Lon,
                RouteManeuvers[nextIndex].Latitude,
                RouteManeuvers[nextIndex].Longitude,
                DistanceUnits.Kilometers) * 1000;
        }

        var nextManeuver = RouteManeuvers[nextIndex];

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
        UpdateGuidanceBanner(nextIndex, nextDistance);
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
            OffRouteStatusText = "Praćenje skretanja sa rute je aktivno";
            return;
        }

        var distanceMeters = CalculateDistanceToPolylineMeters(CurrentLocation, RoutePolylinePoints);
        OffRouteDistanceMeters = distanceMeters;
        IsOffRoute = distanceMeters > OffRouteThresholdMeters;
        OffRouteStatusText = IsOffRoute
            ? $"VAN RUTE · {distanceMeters:0} m"
            : $"NA RUTI · odstupanje {distanceMeters:0} m";

        if (!IsOffRoute)
        {
            hasBeenOnRoute = true;
        }
        else if (!hasBeenOnRoute)
        {
            OffRouteStatusText = $"Vozilo nije na planiranoj ruti ({distanceMeters / 1000:0.#} km od nje)";
            return;
        }

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
