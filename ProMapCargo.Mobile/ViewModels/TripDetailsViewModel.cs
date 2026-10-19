using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

public sealed record TripStopDetailItem(
    Guid Id,
    string SequenceText,
    string Title,
    string Detail,
    string StatusText,
    string? Notes)
{
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
}

public sealed record TripCostDetailItem(
    Guid Id,
    string Title,
    string Detail,
    string AmountText,
    string? Notes,
    Guid? ReceiptFileId)
{
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasReceipt => ReceiptFileId.HasValue;
}

public sealed class TripDetailsViewModel : ViewModelBase, IQueryAttributable
{
    private readonly MobileSessionService sessionService;
    private readonly ApiClient apiClient;
    private readonly DriverPortalStore portalStore;
    private readonly IMobileNotifier notifier;
    private DriverPortalTripSummary? selectedTrip;
    private DriverPortal? portal;
    private Guid tripId;
    private Guid requestedTripId;
    private bool isBusy;
    private bool isPullRefreshing;
    private string statusMessage = "Učitavanje detalja ture…";

    public TripDetailsViewModel(MobileSessionService sessionService, ApiClient apiClient, DriverPortalStore portalStore, IMobileNotifier notifier)
    {
        this.sessionService = sessionService;
        this.apiClient = apiClient;
        this.portalStore = portalStore;
        this.notifier = notifier;

        RefreshCommand = new Command(async () =>
        {
            await LoadAsync();
            IsPullRefreshing = false;
        }, () => !IsBusy);
        OpenNavigationCommand = new Command(OpenNavigation, () => CanOpenNavigation);
        CallDispatchCommand = new Command(CallDispatch, () => HasDispatchPhone);
        OpenReceiptCommand = new Command<Guid>(OpenReceipt);
    }

    public ObservableCollection<TripStopDetailItem> Stops { get; } = new();

    public ObservableCollection<TripCostDetailItem> Costs { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand OpenNavigationCommand { get; }

    public ICommand CallDispatchCommand { get; }

    public ICommand OpenReceiptCommand { get; }

    public string TripTitle => selectedTrip?.TripNumber is { Length: > 0 } number ? $"Tura {number}" : "Detalji ture";

    public string TripStatusText => selectedTrip is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(selectedTrip.Status)
        ? "Status nije poznat"
        : selectedTrip.Status switch
        {
            "Planned" => "Planirana",
            "Active" => DescribeState(selectedTrip.State),
            "Paused" => "Pauza",
            "Completed" => "Završena",
            "Cancelled" => "Otkazana",
            _ => selectedTrip.Status
        };

    public string RouteText => string.IsNullOrWhiteSpace(selectedTrip?.Route) ? "Ruta nije definisana" : selectedTrip.Route;

    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            if (selectedTrip?.PlannedStartAt is { } plannedStart)
            {
                parts.Add($"Plan {plannedStart.ToLocalTime():dd.MM. HH:mm}");
            }

            if (selectedTrip?.StartedAt is { } startedAt)
            {
                parts.Add($"Krenuo {startedAt.ToLocalTime():dd.MM. HH:mm}");
            }

            if (selectedTrip?.PlannedArrivalAt is { } plannedArrival)
            {
                parts.Add($"Dolazak {plannedArrival.ToLocalTime():dd.MM. HH:mm}");
            }

            if (portal?.Trip?.Vehicle is { Length: > 0 } vehicle && tripId == portal.Trip.Id)
            {
                parts.Add(portal.Trip.Trailer is { Length: > 0 } trailer ? $"{vehicle} + {trailer}" : vehicle);
            }

            return parts.Count == 0 ? "Nema vremenskih podataka za ovu turu." : string.Join(" · ", parts);
        }
    }

    public double ProgressValue => Math.Clamp((selectedTrip?.RouteProgressPercent ?? 0d) / 100d, 0d, 1d);

    public string ProgressText
    {
        get
        {
            if (tripId == portal?.Trip?.Id && portal?.Stops is { Count: > 0 } portalStops)
            {
                var done = portalStops.Count(stop => stop.Status is "Completed" or "Skipped");
                return $"{done} od {portalStops.Count} stanica";
            }

            return $"Napredak {Math.Round((selectedTrip?.RouteProgressPercent ?? 0d), 0)}%";
        }
    }

    public string NextStopTitle => GetNextStop() is { } stop ? StopTitle(stop) : "Nema aktivne naredne stanice";

    public string NextStopDetail => GetNextStop() is { } stop ? StopDetail(stop) : "Za ovu turu nisu dostupne detaljne stanice.";

    public bool CanOpenNavigation => GetNextStop() is { Latitude: not null, Longitude: not null };

    public bool HasDispatchPhone => !string.IsNullOrWhiteSpace(portal?.DispatchPhone);

    /// <summary>A trip is shown (the cards); without one the page shows one message.</summary>
    public bool HasTrip => selectedTrip is not null;

    public bool HasNoTrip => selectedTrip is null;

    public string NotesText => string.IsNullOrWhiteSpace(portal?.Notes) ? "Nema posebnih napomena za ovu turu." : portal.Notes!;

    public string FinancialSummary
    {
        get
        {
            var currency = GetCurrency();
            var spent = selectedTrip?.Spent ?? 0m;
            var budget = selectedTrip?.Budget;
            return budget is { } budgetAmount
                ? $"Trošak {FormatMoney(spent, currency)} · budžet {FormatMoney(budgetAmount, currency)}"
                : $"Trošak {FormatMoney(spent, currency)}";
        }
    }

    public string EarningsSummary
    {
        get
        {
            if (tripId != portal?.Trip?.Id || portal?.Financial is null)
            {
                return "Dnevnice i isplate su dostupne na aktivnoj turi.";
            }

            var financial = portal.Financial;
            var earning = financial.DriverEarningAmount is decimal amount ? $"Zarada {FormatMoney(amount, financial.Currency)} · " : "";
            return $"{earning}Dnevnice {FormatMoney(financial.AllowanceAmount, financial.Currency)} · isplaćeno {FormatMoney(financial.PaidOutAmount, financial.Currency)} · na čekanju {FormatMoney(financial.PendingAmount, financial.Currency)}{Environment.NewLine}{AdvanceText(financial)}";
        }
    }

    // The advance against the business expenses the driver paid himself.
    private static string AdvanceText(DriverPortalFinancial financial)
    {
        if (financial.AdvanceAmount is not decimal advance)
        {
            return $"Bez akontacije · vaši poslovni troškovi {FormatMoney(financial.AdvanceSpentAmount, financial.Currency)}";
        }

        var balance = financial.AdvanceBalance ?? 0;
        var settlement = balance > 0
            ? $"vraćate {FormatMoney(balance, financial.Currency)}"
            : balance < 0 ? $"firma vam duguje {FormatMoney(-balance, financial.Currency)}" : "izmireno";
        return $"Akontacija {FormatMoney(advance, financial.Currency)} · opravdano {FormatMoney(financial.AdvanceSpentAmount, financial.Currency)} · {settlement}";
    }

    public string DriverHistorySummary => SummarizeHistory(portal?.DriverHistory, "vozača");

    public string VehicleHistorySummary => SummarizeHistory(portal?.VehicleHistory, "vozila");

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
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

    public bool IsPullRefreshing
    {
        get => isPullRefreshing;
        set => SetProperty(ref isPullRefreshing, value);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("tripId", out var raw) && TryParseGuid(raw, out var parsed))
        {
            requestedTripId = parsed;
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            await sessionService.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
            var userId = sessionService.CurrentSession?.User.Id;

            // Fresh from the server when there is a connection (costs approved meanwhile,
            // stops completed on the web), else the copy saved on the phone.
            var online = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                if (userId is { } id && await apiClient.GetDriverPortalAsync(timeout.Token).ConfigureAwait(false) is { } fresh)
                {
                    portalStore.Save(fresh, id);
                    online = true;
                }
            }
            catch (Exception)
            {
                // No signal or not a driver: the saved copy below.
            }

            portal = userId is { } user ? portalStore.Load(user)?.Portal : null;
            tripId = requestedTripId != Guid.Empty
                ? requestedTripId
                : portal?.Trip?.Id ?? portal?.Trips?.FirstOrDefault()?.Id ?? Guid.Empty;
            if (tripId == Guid.Empty)
            {
                selectedTrip = null;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Stops.Clear();
                    Costs.Clear();
                    RaiseAll();
                    StatusMessage = sessionService.IsSignedIn
                        ? "Trenutno nemate dodeljenu turu."
                        : "Prijavite se kao vozač da vidite svoju turu.";
                });
                return;
            }

            selectedTrip = portal?.Trips?.FirstOrDefault(item => item.Id == tripId)
                ?? (portal?.Trip?.Id == tripId ? MapActiveTrip(portal.Trip, portal.Financial) : null);

            if (selectedTrip is null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Stops.Clear();
                    Costs.Clear();
                    RaiseAll();
                    StatusMessage = "Detalji ture nisu dostupni na ovom telefonu. Osvežite početni ekran vozača.";
                });
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ReplaceItems(Stops, BuildStops());
                ReplaceItems(Costs, BuildCosts());
                RaiseAll();
                StatusMessage = (tripId == portal?.Trip?.Id ? "Aktivna tura" : "Istorija ture") +
                                (online ? $" · osveženo {DateTime.Now:HH:mm}" : " · sačuvana kopija, bez veze");
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyList<TripStopDetailItem> BuildStops()
    {
        if (tripId == portal?.Trip?.Id && portal?.Stops is { Count: > 0 } stops)
        {
            return stops.Select(stop => new TripStopDetailItem(
                stop.Id,
                $"#{stop.Sequence}",
                StopTitle(stop),
                StopDetail(stop),
                StopStatus(stop),
                stop.Notes)).ToList();
        }

        return [];
    }

    private IReadOnlyList<TripCostDetailItem> BuildCosts()
    {
        var costs = selectedTrip?.Costs ?? [];
        return costs.Select(cost => new TripCostDetailItem(
            cost.Id,
            string.IsNullOrWhiteSpace(cost.Category) ? "Trošak" : cost.Category,
            BuildCostDetail(cost),
            FormatMoney(cost.Amount, cost.Currency),
            cost.Notes,
            cost.ReceiptFileId)).ToList();
    }

    private string BuildCostDetail(DriverPortalCost cost)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(cost.PaymentMethod))
        {
            parts.Add(cost.PaymentMethod);
        }

        if (!string.IsNullOrWhiteSpace(cost.CardLabel))
        {
            parts.Add(cost.CardLabel!);
        }

        if (!string.IsNullOrWhiteSpace(cost.Location))
        {
            parts.Add(cost.Location!);
        }

        parts.Add(cost.At.ToLocalTime().ToString("dd.MM. HH:mm", CultureInfo.GetCultureInfo("sr-Latn-RS")));
        return string.Join(" · ", parts);
    }

    private void OpenNavigation()
    {
        if (GetNextStop() is not { Latitude: { } lat, Longitude: { } lon } stop)
        {
            return;
        }

        NavigationRequests.RouteTo(new RecentDestination(
            StopTitle(stop),
            lat,
            lon,
            DateTimeOffset.Now,
            StopAddress(stop)));
        SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Navigation));
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

    // Downloaded with the driver's token and opened from the phone (PDF viewer, gallery).
    private void OpenReceipt(Guid receiptFileId) => SafeUi.Run(async () =>
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var (bytes, contentType, fileName) = await apiClient.DownloadFileAsync(receiptFileId, timeout.Token).ConfigureAwait(false);
            var extension = contentType switch
            {
                "application/pdf" => ".pdf",
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                _ => Path.GetExtension(fileName ?? string.Empty),
            };
            var path = Path.Combine(FileSystem.CacheDirectory, $"racun-{receiptFileId:N}{extension}");
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
                Launcher.Default.OpenAsync(new OpenFileRequest("Račun", new ReadOnlyFile(path, contentType))));
        }
        catch (Exception ex)
        {
            await notifier.ShowErrorAsync($"Račun nije otvoren: {ApiClient.DescribeError(ex)}").ConfigureAwait(false);
        }
    });

    private static DriverPortalTripSummary MapActiveTrip(DriverPortalTrip trip, DriverPortalFinancial? financial)
        => new(
            trip.Id,
            trip.TripNumber,
            trip.Status,
            trip.State,
            trip.Route,
            null,
            trip.PlannedArrivalAt,
            null,
            null,
            trip.RouteProgressPercent,
            financial?.BudgetAmount,
            financial?.SpentAmount ?? 0m,
            financial?.Costs);

    private DriverPortalStop? GetNextStop()
        => tripId == portal?.Trip?.Id
            ? portal?.Stops?.FirstOrDefault(stop => stop.Id == portal.NextStopId)
            : null;

    private string GetCurrency()
        => selectedTrip?.Costs?.FirstOrDefault()?.Currency
           ?? portal?.Financial?.Currency
           ?? "EUR";

    private void RaiseAll()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(TripTitle), nameof(TripStatusText), nameof(RouteText), nameof(SummaryText), nameof(ProgressValue), nameof(ProgressText),
                     nameof(NextStopTitle), nameof(NextStopDetail), nameof(CanOpenNavigation), nameof(HasDispatchPhone), nameof(NotesText),
                     nameof(FinancialSummary), nameof(EarningsSummary), nameof(DriverHistorySummary), nameof(VehicleHistorySummary),
                     nameof(HasTrip), nameof(HasNoTrip)
                 })
        {
            RaisePropertyChanged(propertyName);
        }

        (OpenNavigationCommand as Command)?.ChangeCanExecute();
        (CallDispatchCommand as Command)?.ChangeCanExecute();
    }

    private static bool TryParseGuid(object raw, out Guid guid)
    {
        guid = raw switch
        {
            Guid value => value,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => Guid.Empty
        };

        return guid != Guid.Empty;
    }

    private static string StopTitle(DriverPortalStop stop)
        => !string.IsNullOrWhiteSpace(stop.Name) ? stop.Name : stop.City ?? $"Stanica {stop.Sequence}";

    private static string StopAddress(DriverPortalStop stop)
        => !string.IsNullOrWhiteSpace(stop.Address)
            ? stop.Address!
            : !string.IsNullOrWhiteSpace(stop.Name)
                ? stop.Name
                : stop.City ?? $"Stanica {stop.Sequence}";

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
        _ => "Stanica"
    };

    private static string StopStatus(DriverPortalStop stop) => stop.Status switch
    {
        "Arrived" => "Na stanici",
        "InService" => "U toku",
        "Completed" => "Završeno",
        "Skipped" => "Preskočeno",
        _ => "Planirano"
    };

    private static string DescribeState(string? state) => state switch
    {
        "Loading" => "Utovar",
        "Loaded" => "Utovareno",
        "Unloading" => "Istovar",
        "ApproachingStop" => "Prilazi stanici",
        "AtStop" => "Na stanici",
        "Waiting" => "Čeka",
        "BorderCrossing" => "Granični prelaz",
        _ => "U vožnji"
    };

    private static string FormatMoney(decimal amount, string? currency)
        => $"{amount.ToString("N2", CultureInfo.GetCultureInfo("sr-Latn-RS"))} {currency ?? "EUR"}";

    private static string SummarizeHistory(IReadOnlyList<DriverPortalHistoryItem>? items, string subject)
    {
        if (items is not { Count: > 0 })
        {
            return $"Istorija {subject} nije dostupna.";
        }

        return string.Join(Environment.NewLine, items.Take(5).Select(item =>
            $"{(string.IsNullOrWhiteSpace(item.TripNumber) ? "Tura" : item.TripNumber)} · {item.Route ?? item.OrderNumber ?? "bez rute"} · {item.Status}"));
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
