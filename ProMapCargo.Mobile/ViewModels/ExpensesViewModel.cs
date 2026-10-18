using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>An expense in the list: description, price, payment, status.</summary>
public sealed record ExpenseListItem(
    string Title,
    string Description,
    string AmountText,
    string PaymentText,
    string StatusText,
    bool IsPending,
    bool IsApproved,
    bool IsRejected,
    Guid? ReceiptFileId)
{
    public bool HasReceipt => ReceiptFileId is not null;
}

/// <summary>
/// "Troškovi": what the driver spent on trips (sent and approved by dispatch, or still
/// waiting for a connection), with "Skeniraj račun" and "Novi trošak". The list comes from
/// the trip data the phone keeps, so it shows without signal too.
/// </summary>
public sealed class ExpensesViewModel : ViewModelBase
{
    private static readonly CultureInfo Sr = CultureInfo.GetCultureInfo("sr-Latn-RS");
    private readonly DriverPortalStore portalStore;
    private readonly MobileSessionService session;
    private readonly ExpenseQueue queue;
    private readonly ApiClient apiClient;
    private readonly IMobileNotifier notifier;
    private string summaryText = string.Empty;
    private string statusText = string.Empty;
    private bool isPullRefreshing;

    public ExpensesViewModel(DriverPortalStore portalStore, MobileSessionService session, ExpenseQueue queue, ApiClient apiClient, IMobileNotifier notifier)
    {
        this.portalStore = portalStore;
        this.session = session;
        this.queue = queue;
        this.apiClient = apiClient;
        this.notifier = notifier;
        RefreshCommand = new Command(async () =>
        {
            await RefreshAsync();
            IsPullRefreshing = false;
        });
        NewExpenseCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Expense));
        ScanReceiptCommand = new Command(async () => await AppNavigation.GoToAsync($"{AppNavigation.Expense}?scan=1"));
        queue.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Load);
    }

    public ObservableCollection<ExpenseListItem> Expenses { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand NewExpenseCommand { get; }

    public ICommand ScanReceiptCommand { get; }

    public bool IsEmpty => Expenses.Count == 0;

    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsPullRefreshing
    {
        get => isPullRefreshing;
        set => SetProperty(ref isPullRefreshing, value);
    }

    /// <summary>From the phone: pending ones first, then the trips' costs, newest first.</summary>
    public void Load()
    {
        var items = new List<ExpenseListItem>();
        foreach (var draft in queue.PendingDrafts())
        {
            items.Add(new ExpenseListItem(
                CategoryText(draft.Category),
                Describe(draft.Notes, draft.Location, draft.IncurredAt),
                Money(draft.Amount, draft.Currency),
                PaymentText(draft.PaymentMethod, draft.CardLabel),
                "Čeka slanje",
                true, false, false, null));
        }

        var portal = session.CurrentSession?.User.Id is { } userId ? portalStore.Load(userId)?.Portal : null;
        var costs = (portal?.Trips ?? [])
            .SelectMany(trip => trip.Costs ?? [])
            .Where(cost => cost.Category is not ("Allowance" or "Payment"))
            .OrderByDescending(cost => cost.At)
            .ToList();
        foreach (var cost in costs)
        {
            items.Add(new ExpenseListItem(
                CategoryText(cost.Category),
                Describe(cost.Notes, cost.Location, cost.At),
                Money(cost.Amount, cost.Currency),
                PaymentText(cost.PaymentMethod, cost.CardLabel),
                cost.Status switch
                {
                    "Approved" => "Odobreno",
                    "Rejected" => "Odbijeno",
                    "Paid" => "Isplaćeno",
                    _ => "Na proveri",
                },
                false,
                cost.Status is "Approved" or "Paid",
                cost.Status == "Rejected",
                cost.ReceiptFileId));
        }

        Expenses.Clear();
        foreach (var item in items)
        {
            Expenses.Add(item);
        }

        var monthStart = new DateTimeOffset(DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, DateTimeOffset.Now.Offset);
        var thisMonth = costs.Where(cost => cost.At >= monthStart && cost.Status != "Rejected")
            .GroupBy(cost => cost.Currency)
            .Select(group => Money(group.Sum(cost => cost.Amount), group.Key));
        var pending = queue.PendingCount;
        SummaryText = string.Join(" · ", new[]
        {
            costs.Count == 0 && pending == 0 ? "Još nema troškova" : $"Ovog meseca: {string.Join(" + ", thisMonth.DefaultIfEmpty("0"))}",
            pending > 0 ? $"čeka slanje: {pending}" : null,
        }.Where(part => part is not null));
        RaisePropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Fresh trip costs from the server (approvals by dispatch), then the list.</summary>
    public async Task RefreshAsync()
    {
        Load();
        try
        {
            await queue.FlushAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            if (session.CurrentSession?.User.Id is { } userId &&
                await apiClient.GetDriverPortalAsync(timeout.Token).ConfigureAwait(false) is { } portal)
            {
                portalStore.Save(portal, userId);
            }

            await MainThread.InvokeOnMainThreadAsync(() => StatusText = $"Osveženo {DateTime.Now:HH:mm}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = "Nalog nije povezan sa vozačem: troškove unosi vozač.");
        }
        catch (Exception)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusText = "Bez veze · prikazani su sačuvani troškovi");
        }

        await MainThread.InvokeOnMainThreadAsync(Load);
    }

    /// <summary>The receipt, downloaded with the driver's token and opened on the phone.</summary>
    public async Task OpenReceiptAsync(Guid fileId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var (bytes, contentType, _) = await apiClient.DownloadFileAsync(fileId, timeout.Token).ConfigureAwait(false);
            var extension = contentType switch { "application/pdf" => ".pdf", "image/png" => ".png", _ => ".jpg" };
            var path = Path.Combine(FileSystem.CacheDirectory, $"racun-{fileId:N}{extension}");
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
                Launcher.Default.OpenAsync(new OpenFileRequest("Račun", new ReadOnlyFile(path, contentType))));
        }
        catch (Exception ex)
        {
            await notifier.ShowErrorAsync($"Račun nije otvoren: {ApiClient.DescribeError(ex)}").ConfigureAwait(false);
        }
    }

    private static string CategoryText(string category) => category switch
    {
        "Fuel" => "Gorivo",
        "AdBlue" => "AdBlue",
        "Toll" => "Putarina",
        "Parking" => "Parking",
        "Border" => "Granica",
        "Ferry" => "Trajekt",
        "Food" => "Hrana",
        "Accommodation" => "Smeštaj",
        "Maintenance" => "Servis",
        "Waiting" => "Čekanje",
        _ => "Ostalo",
    };

    private static string PaymentText(string? method, string? card)
    {
        var text = method switch
        {
            "cash" => "Gotovina",
            "driver-card" => "Lična kartica",
            "fuel-card" => "Fuel kartica",
            "company-card" => "Službena kartica",
            _ => "Plaćanje nije navedeno",
        };
        return string.IsNullOrWhiteSpace(card) ? text : $"{text} ({card})";
    }

    private static string Describe(string? notes, string? location, DateTimeOffset at)
        => string.Join(" · ", new[] { at.ToLocalTime().ToString("dd.MM. HH:mm", Sr), location, notes }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Money(decimal amount, string? currency)
        => $"{amount.ToString("N2", Sr)} {currency ?? "EUR"}";
}
