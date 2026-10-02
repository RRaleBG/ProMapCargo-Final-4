using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ApiClient apiClient;
    private readonly MobileSessionService sessionService;
    private readonly IMobileNotifier notifier;
    private CancellationTokenSource? loadCancellationTokenSource;
    private bool isBusy;
    private string statusText = "Ready";
    private string routeSummary = "No route loaded.";
    private string routeEngine = "Engine: —";
    private string routeSafety = "Truck review pending";
    private string routeDiagnosticsSummary = "No diagnostics available.";
    private string routeStartSnap = "—";
    private string routeEndSnap = "—";
    private string routeFailureReason = "—";


    public DashboardViewModel(ApiClient apiClient, MobileSessionService sessionService, IMobileNotifier notifier)
    {
        this.apiClient = apiClient;
        this.sessionService = sessionService;
        this.notifier = notifier;

        Vehicles = new ObservableCollection<VehicleItem>();
        Drivers = new ObservableCollection<DriverItem>();
        Orders = new ObservableCollection<TransportOrderItem>();
        Trips = new ObservableCollection<TripItem>();
        RouteHighlights = new ObservableCollection<string>();
        RouteManeuvers = new ObservableCollection<RouteManeuver>();
        QuickLinks =
        [
            new DashboardLink("Navigation", "Truck-aware route planning and maneuver guidance", "//dashboard/mobile-navigation"),
            new DashboardLink("Settings", "Theme, appearance, account, and map bundle controls", "//dashboard/settings"),
            new DashboardLink("Dispatch", "Order assignment and fleet dispatch", null),
            new DashboardLink("Monitoring", "Live telemetry and vehicle status", null),
            new DashboardLink("Trips", "Trip execution status and progress", null),
            new DashboardLink("Orders", "Active and pending customer orders", null),
            new DashboardLink("Vehicles", "Vehicle availability and health", null),
            new DashboardLink("Drivers", "Driver roster and compliance", null),
            new DashboardLink("Alerts", "Operational alerts and incidents", null)
        ];

        RefreshCommand = new Command(async () => await LoadAsync(), () => !IsBusy);
        OpenLinkCommand = new Command<DashboardLink>(async link => await OpenLinkAsync(link));
    }

    public ObservableCollection<VehicleItem> Vehicles { get; }

    public ObservableCollection<DriverItem> Drivers { get; }

    public ObservableCollection<TransportOrderItem> Orders { get; }

    public ObservableCollection<TripItem> Trips { get; }

    public ObservableCollection<string> RouteHighlights { get; }

    public ObservableCollection<RouteManeuver> RouteManeuvers { get; }

    public IReadOnlyList<DashboardLink> QuickLinks { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenLinkCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
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

    public string WelcomeText => sessionService.CurrentSession?.User.DisplayName is { Length: > 0 } displayName
        ? $"Welcome, {displayName}"
        : "Welcome";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value) && RefreshCommand is Command command)
            {
                command.ChangeCanExecute();
            }
        }
    }

    public async Task LoadAsync()
    {
        loadCancellationTokenSource?.Cancel();
        loadCancellationTokenSource?.Dispose();
        loadCancellationTokenSource = new CancellationTokenSource();

        try
        {
            await SetBusyAsync(true).ConfigureAwait(false);
            StatusText = "Loading operations data...";

            var cancellationToken = loadCancellationTokenSource.Token;
            var vehiclesTask = apiClient.GetVehiclesAsync(cancellationToken);
            var driversTask = apiClient.GetDriversAsync(cancellationToken);
            var ordersTask = apiClient.GetOrdersAsync(cancellationToken);
            var tripsTask = apiClient.GetTripsAsync(cancellationToken);
            var routeTask = apiClient.GetRouteAsync(
                new RouteRequest(
                    new GeoPoint(44.8176, 20.4633),
                    new GeoPoint(45.2671, 19.8335)),
                cancellationToken);

            await Task.WhenAll(vehiclesTask, driversTask, ordersTask, tripsTask, routeTask).ConfigureAwait(false);

            var vehicles = await vehiclesTask.ConfigureAwait(false);
            var drivers = await driversTask.ConfigureAwait(false);
            var orders = await ordersTask.ConfigureAwait(false);
            var trips = await tripsTask.ConfigureAwait(false);
            var route = await routeTask.ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ReplaceItems(Vehicles, vehicles);
                ReplaceItems(Drivers, drivers);
                ReplaceItems(Orders, orders);
                ReplaceItems(Trips, trips);
                ApplyRoute(route);
                RaisePropertyChanged(nameof(WelcomeText));
                StatusText = $"Synced {DateTime.Now:t} · {sessionService.LastStatus}";
            });

            await notifier.ShowSuccessAsync("Operations synced successfully.").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusText = "Sync canceled.";
            });

            await notifier.ShowInfoAsync("Sync canceled.").ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusText = $"Sync failed: {ex.Message} · {sessionService.LastStatus}";
            });

            await notifier.ShowErrorAsync("Sync failed due to network/API error.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusText = $"Sync failed: {ex.Message}";
            });

            await notifier.ShowWarningAsync("Sync failed with unexpected issue.").ConfigureAwait(false);
        }
        finally
        {
            await SetBusyAsync(false).ConfigureAwait(false);
            loadCancellationTokenSource?.Dispose();
            loadCancellationTokenSource = null;
        }
    }

    private Task SetBusyAsync(bool value) => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private void ApplyRoute(RouteResponse? route)
    {
        var selectedRoute = route?.Routes.Count > 0
            ? route.Routes[Math.Clamp(route.SelectedRouteIndex, 0, route.Routes.Count - 1)]
            : null;
        var debug = selectedRoute?.Analysis?.Debug;
        var diagnostics = route?.Diagnostics;
        var highlights = debug?.Highlights?.Count > 0
            ? debug.Highlights
            : diagnostics?.Highlights ?? [];
        var violations = selectedRoute?.Analysis?.Violations?.Count > 0
            ? selectedRoute.Analysis.Violations
            : route?.Violations ?? [];
        var distanceMeters = selectedRoute?.Distance ?? route?.Summary?.DistanceMeters ?? 0;
        var durationSeconds = selectedRoute?.Duration ?? route?.Summary?.DurationSeconds ?? 0;

        RouteSummary = selectedRoute is null
            ? "Route service available but no route is currently selected."
            : $"Belgrade → Novi Sad · {distanceMeters / 1000:0.#} km · {durationSeconds / 60:0} min";

        RouteEngine = diagnostics is null
            ? "Engine: —"
            : diagnostics.UsedFallback
                ? $"Engine: {diagnostics.Engine} · FALLBACK"
                : $"Engine: {diagnostics.Engine}";

        RouteSafety = route is null
            ? "Truck review pending"
            : route.IsTruckSafe
                ? "Truck safe"
                : violations.Count > 0
                    ? $"Restriction warnings: {violations.Count}"
                    : "Route needs review";

        var diagnosticParts = new List<string>();
        if (diagnostics is not null && diagnostics.ExpandedStates > 0)
        {
            diagnosticParts.Add($"{diagnostics.ExpandedStates} states");
        }

        var traversalCount = debug?.TraversalCount > 0 ? debug.TraversalCount : diagnostics?.TraversalCount ?? 0;
        if (traversalCount > 0)
        {
            diagnosticParts.Add($"{traversalCount} traversals");
        }

        if (diagnostics?.GraphVersion is not null)
        {
            diagnosticParts.Add($"graph {diagnostics.GraphVersion}");
        }

        if (diagnostics?.UsedFallback == true)
        {
            diagnosticParts.Add("fallback active");
        }

        RouteDiagnosticsSummary = diagnosticParts.Count > 0
            ? string.Join(" · ", diagnosticParts)
            : debug?.Summary ?? "No diagnostics available.";

        RouteStartSnap = debug?.StartSnap ?? diagnostics?.StartSnap ?? "—";
        RouteEndSnap = debug?.EndSnap ?? diagnostics?.EndSnap ?? "—";
        RouteFailureReason = diagnostics?.FailureReason ?? "—";

        ReplaceItems(RouteHighlights, highlights);
        ReplaceItems(RouteManeuvers, route?.Maneuvers ?? []);
    }

    private async Task OpenLinkAsync(DashboardLink? link)
    {
        if (link is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(link.ShellRoute))
        {
            StatusText = $"{link.Title} is available on the web portal. Native mobile screen is coming soon.";
            await notifier.ShowInfoAsync(StatusText).ConfigureAwait(false);
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var shell = Shell.Current;
            if (shell is null)
            {
                return;
            }

            await shell.GoToAsync(link.ShellRoute);
        });
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    public sealed record DashboardLink(string Title, string Description, string? ShellRoute);
}
