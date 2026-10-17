using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>A stop of the driver's trip as shown on the home screen.</summary>
public sealed record TripStopItem(
    Guid Id,
    int Sequence,
    string Title,
    string Detail,
    string StatusText,
    bool IsDone,
    bool IsNext,
    bool IsLast);

public sealed record DriverTripListItem(
    Guid Id,
    string Title,
    string Route,
    string StatusText,
    string TimelineText,
    string FinancialText,
    bool IsCurrent,
    DriverPortalTripSummary Source);

/// <summary>
/// Home screen: search, the driver's trip (stops, arrival and completion, break, incident,
/// a route sent by dispatch), recent destinations, truck profile and driving bans. The trip
/// is kept on the phone, so it is shown without signal; actions need the server.
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ApiClient apiClient;
    private readonly OfflineWebCache offlineWebCache;
    private readonly OfflineRoutingBundleService offlineBundles;
    private readonly MobileSessionService sessionService;
    private readonly IMobileNotifier notifier;
    private readonly DriverPortalStore portalStore;
    private readonly GpsTelemetryUploader telemetry;
    private string? announcedBundleVersion;

    private CancellationTokenSource? loadCancellationTokenSource;
    private bool isBusy;
    private bool isActionRunning;
    private bool isOffline;
    private bool isNotDriver;
    private string statusText = "Spremno";
    private string fleetSummary = string.Empty;
    private DriverPortal? portal;
    private DateTimeOffset? portalSavedAt;
    private TruckBanSummary.Result truckBans = TruckBanSummary.Build(TruckBanSummary.LoadSaved(), DateTimeOffset.Now);

    public DashboardViewModel(
        ApiClient apiClient,
        MobileSessionService sessionService,
        IMobileNotifier notifier,
        OfflineWebCache offlineWebCache,
        OfflineRoutingBundleService offlineBundles,
        DriverPortalStore portalStore,
        GpsTelemetryUploader telemetry)
    {
        this.offlineBundles = offlineBundles;
        this.offlineWebCache = offlineWebCache;
        this.apiClient = apiClient;
        this.sessionService = sessionService;
        this.notifier = notifier;
        this.portalStore = portalStore;
        this.telemetry = telemetry;

        RefreshCommand = new Command(async () =>
        {
            await LoadAsync();
            IsPullRefreshing = false;
        }, () => !IsBusy);
        SignInCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Login));
        NavigateToNextStopCommand = new Command(NavigateToNextStop, () => NextStop?.Latitude is not null && NextStop.Longitude is not null);
        CallDispatchCommand = new Command(CallDispatch);
        OpenTripDetailsCommand = new Command<DriverTripListItem>(OpenTripDetails);

        telemetry.StatusChanged += (_, _) => RaisePropertyChanged(nameof(GpsStatusText));
    }

    public ICommand RefreshCommand { get; }

    public ICommand SignInCommand { get; }

    public ICommand NavigateToNextStopCommand { get; }

    public ICommand CallDispatchCommand { get; }

    public ICommand OpenTripDetailsCommand { get; }

    /// <summary>Places the driver routed to, newest first (TomTom-style recents).</summary>
    public ObservableCollection<RecentDestination> Recents { get; } = new();

    public ObservableCollection<TripStopItem> Stops { get; } = new();

    public ObservableCollection<DriverTripListItem> DriverTrips { get; } = new();

    public bool HasRecents => Recents.Count > 0;

    public bool HasDriverTrips => DriverTrips.Count > 0;

    // The app sends no truck profile, so routing uses the server default
    // (RoutingController: 40 t, 4.0 m high, 2.55 m wide, 16.5 m long).
    public string TruckProfileText => "40 t · 4,0 m · 2,55 m · 16,5 m";

    // ---- Header ----

    public string WelcomeText => sessionService.CurrentSession?.User.DisplayName is { Length: > 0 } displayName
        ? $"Zdravo, {displayName.Split(' ')[0]}"
        : "Dobrodošli";

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsOffline
    {
        get => isOffline;
        private set
        {
            if (SetProperty(ref isOffline, value))
            {
                RaisePropertyChanged(nameof(ConnectionText));
            }
        }
    }

    public string ConnectionText => IsOffline ? "Bez veze" : "Na mreži";

    // ---- Trip card ----

    public bool IsSignedIn => sessionService.IsSignedIn;

    public bool ShowSignIn => !sessionService.IsSignedIn;

    public bool ShowNotDriver => sessionService.IsSignedIn && isNotDriver;

    public bool HasTrip => sessionService.IsSignedIn && portal?.Trip is not null;

    public bool ShowNoTrip => sessionService.IsSignedIn && !isNotDriver && portal is not null && portal.Trip is null;

    private DriverPortalTrip? Trip => portal?.Trip;

    public string TripTitle => Trip is { } trip ? $"Tura {trip.TripNumber}" : string.Empty;

    public string TripRoute => Trip?.Route is { Length: > 0 } route ? route : "Ruta nije određena";

    public string TripStatusText => Trip is null ? string.Empty : Trip.Status switch
    {
        "Planned" => "Planirana",
        "Active" => DescribeState(Trip.State),
        "Paused" => "Pauza",
        "Completed" => "Završena",
        "Cancelled" => "Otkazana",
        _ => Trip.Status,
    };

    public bool IsTripActive => Trip?.Status == "Active";

    public bool IsTripPaused => Trip?.Status == "Paused";

    public bool IsTripPlanned => Trip?.Status == "Planned";

    public bool CanPause => IsTripActive || IsTripPaused;

    public string PauseButtonText => IsTripPaused ? "Nastavi vožnju" : "Pauza";

    public double TripProgress => Math.Clamp((Trip?.RouteProgressPercent ?? 0) / 100, 0, 1);

    public string TripProgressText
    {
        get
        {
            var stops = portal?.Stops ?? [];
            var done = stops.Count(stop => stop.Status is "Completed" or "Skipped");
            return stops.Count == 0 ? $"{Math.Round(TripProgress * 100)}%" : $"{done} od {stops.Count} stanica";
        }
    }

    public string TripDetailText
    {
        get
        {
            var parts = new List<string>();
            if (Trip?.Vehicle is { Length: > 0 } vehicle) parts.Add(vehicle);
            if (Trip?.PlannedArrivalAt is { } eta) parts.Add($"dolazak {eta.ToLocalTime():dd.MM. HH:mm}");
            if (portal?.Financial?.BudgetAmount is { } budget) parts.Add($"budžet {FormatMoney(budget, portal.Financial.Currency)}");
            return string.Join(" · ", parts);
        }
    }

    public string TripNotesText => string.IsNullOrWhiteSpace(portal?.Notes) ? "Nema posebnih napomena za vozača." : portal.Notes!;

    public string TripFinancialSummary
    {
        get
        {
            var financial = portal?.Financial;
            if (financial is null)
            {
                return "Troškovi nisu dostupni.";
            }

            return $"Troškovi {FormatMoney(financial.SpentAmount, financial.Currency)} · vozač platio {FormatMoney(financial.DriverPaidAmount, financial.Currency)}";
        }
    }

    public string TripEarningsSummary
    {
        get
        {
            var financial = portal?.Financial;
            if (financial is null)
            {
                return "Zarada nije dostupna.";
            }

            return $"Dnevnice {FormatMoney(financial.AllowanceAmount, financial.Currency)} · isplaćeno {FormatMoney(financial.PaidOutAmount, financial.Currency)} · na čekanju {FormatMoney(financial.PendingAmount, financial.Currency)}";
        }
    }

    public string EuStaySummary => portal?.EuStay?.StatusText ?? "EU 90/180 pregled nije dostupan.";

    public string EuStayDetailText
    {
        get
        {
            var euStay = portal?.EuStay;
            if (euStay is null)
            {
                return string.Empty;
            }

            var countries = euStay.Countries is { Count: > 0 } list ? string.Join(", ", list) : "bez EU segmenata";
            return $"{euStay.DaysUsed}/90 dana · preostaje {euStay.DaysRemaining} · period {euStay.WindowStart:dd.MM.yyyy} - {euStay.WindowEnd:dd.MM.yyyy} · {countries}";
        }
    }

    public string CostLedgerSummary => portal?.Financial?.Costs is { Count: > 0 } costs
        ? string.Join(Environment.NewLine, costs.Take(4).Select(cost => $"{cost.Category}: {FormatMoney(cost.Amount, cost.Currency)} · {cost.PaymentMethod ?? "nepoznato"}{(string.IsNullOrWhiteSpace(cost.Location) ? string.Empty : $" · {cost.Location}")}"))
        : "Još nema unetih troškova za ovu turu.";

    public string VehicleRepairsSummary => portal?.VehicleRepairs is { Count: > 0 } repairs
        ? string.Join(Environment.NewLine, repairs.Take(5).Select(repair => $"{repair.Title ?? repair.Kind ?? "Stavka"}: {repair.Status ?? "—"}{(repair.Cost is { } cost ? $" · {FormatMoney(cost, repair.Currency ?? "EUR")}" : string.Empty)}"))
        : "Nema prijavljenih popravki ili beleški za vozilo.";

    /// <summary>When the trip was loaded; says so when it is the saved copy (no signal).</summary>
    public string PortalUpdatedText => portalSavedAt is not { } at
        ? string.Empty
        : IsOffline
            ? $"Sačuvano {at.ToLocalTime():dd.MM. HH:mm} · bez veze"
            : $"Osveženo {at.ToLocalTime():HH:mm}";

    private DriverPortalStop? NextStop => portal?.Stops?.FirstOrDefault(stop => stop.Id == portal.NextStopId);

    public bool HasNextStop => HasTrip && NextStop is not null;

    public string NextStopTitle => NextStop is { } stop ? StopTitle(stop) : string.Empty;

    public string NextStopDetail => NextStop is { } stop ? StopDetail(stop) : string.Empty;

    public string NextStopActionText => NextStop?.Status is "Arrived" or "InService" ? "Završi stanicu" : "Stigao sam";

    public bool CanNavigateToNextStop => NextStop?.Latitude is not null && NextStop.Longitude is not null;

    public bool HasPendingRoute => portal?.PendingRoute is not null;

    public string PendingRouteText => portal?.PendingRoute is { } pending
        ? $"Dispečer je poslao rutu {(pending.SentAt ?? pending.CreatedAt).ToLocalTime():dd.MM. HH:mm}. Prihvatate li je?"
        : string.Empty;

    public bool HasDispatchPhone => !string.IsNullOrWhiteSpace(portal?.DispatchPhone);

    public string GpsStatusText => telemetry.StatusText;

    public bool IsActionRunning
    {
        get => isActionRunning;
        private set => SetProperty(ref isActionRunning, value);
    }

    // ---- Other cards ----

    /// <summary>Fleet numbers for dispatchers and administrators (a driver sees the trip instead).</summary>
    public string FleetSummary
    {
        get => fleetSummary;
        private set
        {
            if (SetProperty(ref fleetSummary, value))
            {
                RaisePropertyChanged(nameof(HasFleetSummary));
            }
        }
    }

    public bool HasFleetSummary => !string.IsNullOrEmpty(FleetSummary);

    public string TruckBansSummary => truckBans.Summary;

    public string TruckBansBadge => truckBans.Badge;

    public bool TruckBanActiveNow => truckBans.ActiveNow;

    public string TruckBansDetails => truckBans.Details;

    // Only pull-to-refresh shows the RefreshView spinner: while a RefreshView is
    // refreshing, Android blocks every tap on the page, so the automatic load on
    // opening the screen must not use it (buttons seemed dead for seconds).
    private bool isPullRefreshing;

    public bool IsPullRefreshing
    {
        get => isPullRefreshing;
        set => SetProperty(ref isPullRefreshing, value);
    }

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

    public void ReloadRecents()
    {
        ReplaceItems(Recents, RecentDestinations.Load());
        RaisePropertyChanged(nameof(HasRecents));
    }

    public async Task LoadAsync()
    {
        // A newer load (page shown again) replaces this one; the replaced load
        // must neither report "cancelled" nor clean up the newer one's state.
        loadCancellationTokenSource?.Cancel();
        var loadCts = new CancellationTokenSource();
        loadCancellationTokenSource = loadCts;

        try
        {
            await SetBusyAsync(true).ConfigureAwait(false);
            var cancellationToken = loadCts.Token;
            await sessionService.RestoreAsync(cancellationToken).ConfigureAwait(false);

            // What is on the phone first: the saved trip and ban calendar (no signal needed).
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusText = "Učitavanje…";
                ApplyTruckBans(TruckBanSummary.LoadSaved());
                if (portal is null && sessionService.CurrentSession?.User.Id is { } userId && portalStore.Load(userId) is { } saved)
                {
                    ApplyPortal(saved.Portal, saved.SavedAt);
                }

                RaiseSessionProperties();
            });

            // Without signal every request would hang until its timeout (and keep
            // threads busy); check once, quickly, and skip the sync when there is no server.
            if (!await apiClient.IsServerReachableAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false))
            {
                offlineWebCache.MarkOffline();
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsOffline = true;
                    RaisePropertyChanged(nameof(PortalUpdatedText));
                    StatusText = "Bez veze sa serverom · prikazani su sačuvani podaci";
                });
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() => IsOffline = false);

            if (sessionService.IsSignedIn)
            {
                await LoadPortalAsync(cancellationToken).ConfigureAwait(false);
            }

            await LoadTruckBansAsync(cancellationToken).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                RaiseSessionProperties();
                StatusText = sessionService.IsSignedIn
                    ? $"Sinhronizovano u {DateTime.Now:HH:mm}"
                    : "Niste prijavljeni · navigacija i mape rade i bez naloga";
            });

            // The offline routing package is rebuilt on the server after a map update;
            // remind once per version to download it while there is a connection.
            if (await offlineBundles.FindNewerOnServerAsync(cancellationToken).ConfigureAwait(false) is { } newer &&
                newer.Version != announcedBundleVersion)
            {
                announcedBundleVersion = newer.Version;
                await notifier.ShowInfoAsync($"Nova verzija offline paketa „{newer.DisplayName}“ (Offline mape → Paketi za rutiranje).").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
            // Superseded by a newer load: nothing to report.
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Sinhronizacija nije uspela: {ApiClient.DescribeError(ex)}");
        }
        finally
        {
            if (ReferenceEquals(loadCancellationTokenSource, loadCts))
            {
                loadCancellationTokenSource = null;
                await SetBusyAsync(false).ConfigureAwait(false);
            }

            loadCts.Dispose();
        }
    }

    private async Task LoadPortalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await apiClient.GetDriverPortalAsync(cancellationToken).ConfigureAwait(false);
            if (loaded is not null && sessionService.CurrentSession?.User.Id is { } userId)
            {
                portalStore.Save(loaded, userId);
            }

            isNotDriver = false;
            await MainThread.InvokeOnMainThreadAsync(() => ApplyPortal(loaded, DateTimeOffset.Now));
            await ApplyTrackingAsync().ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => FleetSummary = string.Empty);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            // Dispatcher or administrator: no trip of their own, the fleet in one line instead.
            isNotDriver = true;
            await telemetry.SetTripAsync(null, null, running: false).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => ApplyPortal(null, null));
            await LoadFleetSummaryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LoadFleetSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var vehicles = apiClient.GetVehiclesAsync(cancellationToken);
            var orders = apiClient.GetOrdersAsync(cancellationToken);
            var trips = apiClient.GetTripsAsync(cancellationToken);
            await Task.WhenAll(vehicles, orders, trips).ConfigureAwait(false);
            var summary = $"{vehicles.Result.Count} vozila · {orders.Result.Count} naloga · {trips.Result.Count} tura";
            await MainThread.InvokeOnMainThreadAsync(() => FleetSummary = summary);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Optional line: left out when the lists cannot be read.
        }
    }

    // A running trip keeps the GPS on and sends the truck's position to dispatch.
    private Task ApplyTrackingAsync()
    {
        var trip = portal?.Trip;
        var vehicleId = trip?.VehicleId ?? portal?.Driver?.CurrentVehicleId;
        var running = trip?.Status is "Active" or "Paused";
        return telemetry.SetTripAsync(trip is null ? null : vehicleId, trip?.Id, running);
    }

    private void ApplyPortal(DriverPortal? value, DateTimeOffset? savedAt)
    {
        portal = value;
        portalSavedAt = savedAt;

        var stops = value?.Stops ?? [];
        ReplaceItems(Stops, stops.Select((stop, index) => new TripStopItem(
            stop.Id,
            stop.Sequence,
            StopTitle(stop),
            StopDetail(stop),
            StopStatus(stop),
            stop.Status is "Completed" or "Skipped",
            stop.Id == value?.NextStopId,
            index == stops.Count - 1)));

        var allTrips = value?.Trips ?? [];
        ReplaceItems(DriverTrips, allTrips.Select(MapDriverTrip));

        (NavigateToNextStopCommand as Command)?.ChangeCanExecute();
        foreach (var name in new[]
                 {
                     nameof(HasTrip), nameof(ShowNoTrip), nameof(ShowNotDriver), nameof(TripTitle), nameof(TripRoute),
                     nameof(TripStatusText), nameof(IsTripActive), nameof(IsTripPaused), nameof(IsTripPlanned),
                     nameof(CanPause), nameof(PauseButtonText), nameof(TripProgress), nameof(TripProgressText),
                     nameof(TripDetailText), nameof(TripNotesText), nameof(TripFinancialSummary), nameof(TripEarningsSummary), nameof(EuStaySummary), nameof(EuStayDetailText), nameof(CostLedgerSummary), nameof(HasDriverTrips), nameof(VehicleRepairsSummary), nameof(PortalUpdatedText), nameof(HasNextStop), nameof(NextStopTitle),
                     nameof(NextStopDetail), nameof(NextStopActionText), nameof(CanNavigateToNextStop),
                     nameof(HasPendingRoute), nameof(PendingRouteText), nameof(HasDispatchPhone), nameof(GpsStatusText),
                  })
        {
            RaisePropertyChanged(name);
        }
    }

    private void RaiseSessionProperties()
    {
        RaisePropertyChanged(nameof(WelcomeText));
        RaisePropertyChanged(nameof(IsSignedIn));
        RaisePropertyChanged(nameof(ShowSignIn));
        RaisePropertyChanged(nameof(ShowNotDriver));
        RaisePropertyChanged(nameof(ShowNoTrip));
        RaisePropertyChanged(nameof(HasTrip));
        RaisePropertyChanged(nameof(GpsStatusText));
    }

    // ---- Driver actions (need the server; the page asks for confirmation first) ----

    public Task NextStopActionAsync()
    {
        if (NextStop is not { } stop)
        {
            return Task.CompletedTask;
        }

        return stop.Status is "Arrived" or "InService"
            ? RunActionAsync(ct => apiClient.CompleteStopAsync(stop.Id, ct), $"Stanica „{StopTitle(stop)}“ je završena.")
            : RunActionAsync(ct => apiClient.ArriveAtStopAsync(stop.Id, ct), $"Dolazak na „{StopTitle(stop)}“ je evidentiran.");
    }

    public Task StartTripAsync()
        => Trip is { } trip
            ? RunActionAsync(ct => apiClient.StartTripAsync(trip.Id, ct), "Tura je započeta. Srećan put!")
            : Task.CompletedTask;

    public Task TogglePauseAsync()
        => RunActionAsync(ct => apiClient.TogglePauseAsync(ct), IsTripPaused ? "Pauza je završena." : "Pauza je započeta.");

    public Task AcceptRouteAsync()
        => portal?.PendingRoute is { } pending
            ? RunActionAsync(ct => apiClient.AcceptRouteAsync(pending.Id, ct), "Ruta je prihvaćena.")
            : Task.CompletedTask;

    public Task RejectRouteAsync(string? reason)
        => portal?.PendingRoute is { } pending
            ? RunActionAsync(ct => apiClient.RejectRouteAsync(pending.Id, reason, ct), "Ruta je odbijena. Dispečer je obavešten.")
            : Task.CompletedTask;

    public Task ReportIncidentAsync(string message, bool critical)
        => RunActionAsync(ct => apiClient.ReportIncidentAsync(message, critical ? "Critical" : "Warning", ct), "Incident je prijavljen dispečeru.");

    public Task SubmitExpenseAsync(string category, decimal amount, string paymentMethod, string? cardLabel, string? location, string? notes, byte[]? receiptBytes, string? receiptFileName, string? receiptContentType)
    {
        if (Trip is null)
        {
            return Task.CompletedTask;
        }

        return RunActionAsync(async ct =>
        {
            await apiClient.AddTripCostAsync(new DriverExpenseDraft(
                category,
                Trip.Id,
                amount,
                DateTimeOffset.Now,
                location,
                NextStop?.CountryCode,
                paymentMethod,
                string.Equals(paymentMethod, "cash", StringComparison.OrdinalIgnoreCase) ? null : cardLabel,
                null,
                null,
                null,
                portal?.Driver?.CurrentVehicleId ?? Trip.VehicleId,
                notes,
                receiptBytes,
                receiptFileName,
                receiptContentType), ct).ConfigureAwait(false);
        }, "Trošak je poslat dispečeru na proveru.");
    }

    public string NextStopConfirmText => NextStop is { } stop
        ? $"{NextStopActionText}: {StopTitle(stop)}?"
        : string.Empty;

    private async Task RunActionAsync(Func<CancellationToken, Task> action, string successMessage)
    {
        if (IsActionRunning)
        {
            return;
        }

        if (IsOffline)
        {
            await notifier.ShowWarningAsync("Nema veze sa serverom. Pokušajte kad se veza vrati.").ConfigureAwait(false);
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsActionRunning = true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await action(timeout.Token).ConfigureAwait(false);
            await notifier.ShowSuccessAsync(successMessage).ConfigureAwait(false);
            await LoadPortalAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await notifier.ShowErrorAsync(ApiClient.DescribeError(ex)).ConfigureAwait(false);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsActionRunning = false);
        }
    }

    private void NavigateToNextStop()
    {
        if (NextStop is not { Latitude: { } lat, Longitude: { } lon } stop)
        {
            return;
        }

        NavigationRequests.RouteTo(new RecentDestination(StopTitle(stop), lat, lon, DateTimeOffset.Now));
        SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Navigation));
    }

    private void OpenTripDetails(DriverTripListItem? trip)
    {
        if (trip is null)
        {
            return;
        }

        SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.GetTripDetailsRoute(trip.Id)));
    }

    private void CallDispatch()
    {
        if (portal?.DispatchPhone is not { Length: > 0 } phone)
        {
            SafeUi.Run(() => notifier.ShowInfoAsync("Broj dispečera nije unet u podešavanjima firme."));
            return;
        }

        try
        {
            if (PhoneDialer.Default.IsSupported)
            {
                PhoneDialer.Default.Open(phone);
            }
        }
        catch (Exception)
        {
            SafeUi.Run(() => notifier.ShowInfoAsync($"Dispečer: {phone}"));
        }
    }

    // ---- Truck bans ----

    private void ApplyTruckBans(TruckBanCalendar? calendar)
    {
        truckBans = TruckBanSummary.Build(calendar, DateTimeOffset.Now);
        RaisePropertyChanged(nameof(TruckBansSummary));
        RaisePropertyChanged(nameof(TruckBansBadge));
        RaisePropertyChanged(nameof(TruckBanActiveNow));
        RaisePropertyChanged(nameof(TruckBansDetails));
    }

    private async Task LoadTruckBansAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await apiClient.GetTruckBansAsync(cancellationToken).ConfigureAwait(false) is { } calendar)
            {
                TruckBanSummary.Save(calendar);
                await MainThread.InvokeOnMainThreadAsync(() => ApplyTruckBans(calendar));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // Keep the saved calendar; it is re-evaluated against the clock.
            await MainThread.InvokeOnMainThreadAsync(() => ApplyTruckBans(TruckBanSummary.LoadSaved()));
        }
    }

    // ---- Texts ----

    private static string StopTitle(DriverPortalStop stop)
        => !string.IsNullOrWhiteSpace(stop.Name) ? stop.Name : stop.City ?? $"Stanica {stop.Sequence}";

    private static string StopDetail(DriverPortalStop stop)
    {
        var parts = new List<string> { StopType(stop.Type) };
        if (!string.IsNullOrWhiteSpace(stop.City) && !string.Equals(stop.City, stop.Name, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(stop.CountryCode is { Length: > 0 } country ? $"{stop.City}, {country}" : stop.City);
        }

        if (stop.CompletedAt is { } completed) parts.Add($"završeno {completed.ToLocalTime():HH:mm}");
        else if (stop.ArrivedAt is { } arrived) parts.Add($"stigao {arrived.ToLocalTime():HH:mm}");
        else if (stop.PlannedAt is { } planned) parts.Add($"plan {planned.ToLocalTime():dd.MM. HH:mm}");
        return string.Join(" · ", parts);
    }

    private static string StopType(string type) => type switch
    {
        "Loading" => "Utovar",
        "Unloading" => "Istovar",
        "LoadingAndUnloading" => "Utovar i istovar",
        "Border" => "Granica",
        "Rest" => "Odmor",
        "Fuel" => "Gorivo",
        _ => "Stanica",
    };

    private static string StopStatus(DriverPortalStop stop) => stop.Status switch
    {
        "Arrived" => "Na stanici",
        "InService" => "U toku",
        "Completed" => "Završeno",
        "Skipped" => "Preskočeno",
        _ => "Planirano",
    };

    private DriverTripListItem MapDriverTrip(DriverPortalTripSummary trip)
    {
        var status = string.IsNullOrWhiteSpace(trip.Status)
            ? "Nepoznato"
            : trip.Status switch
            {
                "Planned" => "Planirana",
                "Active" => DescribeState(trip.State),
                "Paused" => "Pauza",
                "Completed" => "Završena",
                "Cancelled" => "Otkazana",
                _ => trip.Status
            };

        var timeline = trip.StartedAt ?? trip.PlannedStartAt;
        var timelineText = timeline is { } at
            ? $"{(trip.StartedAt is null ? "Polazak" : "Krenuo")} {at.ToLocalTime():dd.MM. HH:mm}"
            : "Vreme nije definisano";

        var currentTripId = Trip?.Id;
        return new DriverTripListItem(
            trip.Id,
            string.IsNullOrWhiteSpace(trip.TripNumber) ? "Tura" : $"Tura {trip.TripNumber}",
            string.IsNullOrWhiteSpace(trip.Route) ? "Ruta nije definisana" : trip.Route,
            status,
            timelineText,
            $"Trošak {FormatMoney(trip.Spent, portal?.Financial?.Currency ?? "EUR")}",
            currentTripId.HasValue && currentTripId.Value == trip.Id,
            trip);
    }

    private static string DescribeState(string? state) => state switch
    {
        "Loading" => "Utovar",
        "Loaded" => "Utovareno",
        "Unloading" => "Istovar",
        "ApproachingStop" => "Prilazi stanici",
        "AtStop" => "Na stanici",
        "Waiting" => "Čeka",
        "BorderCrossing" => "Granični prelaz",
        _ => "U vožnji",
    };

    private static string FormatMoney(decimal amount, string? currency)
        => $"{amount.ToString("N2", CultureInfo.GetCultureInfo("sr-Latn-RS"))} {currency ?? "EUR"}";

    private Task SetBusyAsync(bool value) => MainThread.InvokeOnMainThreadAsync(() => IsBusy = value);

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
