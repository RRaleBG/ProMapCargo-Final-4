using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>An expense category chip on the expense form.</summary>
public sealed class ExpenseCategoryOption(string code, string title, bool hasLitres) : ViewModelBase
{
    private bool isSelected;

    public string Code { get; } = code;

    public string Title { get; } = title;

    /// <summary>Fuel and AdBlue are entered with litres.</summary>
    public bool HasLitres { get; } = hasLitres;

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}

/// <summary>
/// "Novi trošak": one form instead of a chain of dialogs. Category, amount and currency (the
/// currency of the country the truck is in), litres for fuel, payment, odometer, place,
/// note and a receipt (photo, gallery or PDF). Without signal the expense is kept and sent
/// later (<see cref="ExpenseQueue"/>).
/// </summary>
public sealed class ExpenseViewModel : ViewModelBase
{
    private const string LastCurrencyKey = "expense.last_currency";
    private const int MaxReceiptBytes = 10 * 1024 * 1024; // server limit (FileStorageService.MaxBytes)

    private static readonly Dictionary<string, string> CountryCurrency = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RS"] = "RSD", ["HU"] = "HUF", ["RO"] = "RON", ["BG"] = "BGN", ["MK"] = "MKD", ["BA"] = "BAM",
        ["AL"] = "ALL", ["CZ"] = "CZK", ["PL"] = "PLN", ["CH"] = "CHF", ["GB"] = "GBP", ["SE"] = "SEK",
        ["DK"] = "DKK", ["NO"] = "NOK", ["TR"] = "TRY", ["UA"] = "UAH", ["MD"] = "MDL",
    };

    private readonly ExpenseQueue queue;
    private readonly DriverPortalStore portalStore;
    private readonly MobileSessionService session;
    private readonly GpsTracker gpsTracker;
    private readonly IMobileNotifier notifier;
    private DriverPortal? portal;
    private string amountText = string.Empty;
    private string litresText = string.Empty;
    private string odometerText = string.Empty;
    private string cardLabel = string.Empty;
    private string location = string.Empty;
    private string notes = string.Empty;
    private string currency = "EUR";
    private string paymentMethod = PaymentMethods[0];
    private string? errorMessage;
    private bool isBusy;
    private byte[]? receiptBytes;
    private string? receiptFileName;
    private string? receiptContentType;
    private ImageSource? receiptPreview;

    public ExpenseViewModel(ExpenseQueue queue, DriverPortalStore portalStore, MobileSessionService session, GpsTracker gpsTracker, IMobileNotifier notifier)
    {
        this.queue = queue;
        this.portalStore = portalStore;
        this.session = session;
        this.gpsTracker = gpsTracker;
        this.notifier = notifier;

        Categories =
        [
            new("Fuel", "Gorivo", true),
            new("AdBlue", "AdBlue", true),
            new("Toll", "Putarina", false),
            new("Parking", "Parking", false),
            new("Border", "Granica", false),
            new("Ferry", "Trajekt", false),
            new("Food", "Hrana", false),
            new("Accommodation", "Smeštaj", false),
            new("Maintenance", "Servis", false),
            new("Other", "Ostalo", false),
        ];
        Categories[0].IsSelected = true;

        SelectCategoryCommand = new Command<ExpenseCategoryOption>(SelectCategory);
        UseMyLocationCommand = new Command(UseMyLocation);
        RemoveReceiptCommand = new Command(() => SetReceipt(null, null, null));
        SubmitCommand = new Command(async () => await SubmitAsync(), () => !IsBusy);
    }

    public static IReadOnlyList<string> Currencies { get; } =
        ["EUR", "RSD", "HUF", "RON", "BGN", "MKD", "BAM", "ALL", "CZK", "PLN", "CHF", "GBP", "SEK", "DKK", "NOK", "TRY", "UAH", "MDL"];

    public static IReadOnlyList<string> PaymentMethods { get; } = ["Službena kartica", "Fuel kartica", "Gotovina", "Lična kartica vozača"];

    public IReadOnlyList<string> CurrencyOptions => Currencies;

    public IReadOnlyList<string> PaymentOptions => PaymentMethods;

    public ObservableCollection<ExpenseCategoryOption> Categories { get; }

    public ICommand SelectCategoryCommand { get; }

    public ICommand UseMyLocationCommand { get; }

    public ICommand RemoveReceiptCommand { get; }

    public ICommand SubmitCommand { get; }

    public ExpenseCategoryOption SelectedCategory => Categories.First(item => item.IsSelected);

    public bool ShowLitres => SelectedCategory.HasLitres;

    public string TripText => portal?.Trip is { } trip
        ? $"Tura {trip.TripNumber} · {trip.Route}"
        : "Trošak bez ture (vozilo vozača)";

    public string AmountText
    {
        get => amountText;
        set
        {
            if (SetProperty(ref amountText, value))
            {
                RaisePropertyChanged(nameof(UnitPriceText));
            }
        }
    }

    public string Currency
    {
        get => currency;
        set
        {
            if (SetProperty(ref currency, value))
            {
                RaisePropertyChanged(nameof(UnitPriceText));
            }
        }
    }

    public string LitresText
    {
        get => litresText;
        set
        {
            if (SetProperty(ref litresText, value))
            {
                RaisePropertyChanged(nameof(UnitPriceText));
            }
        }
    }

    /// <summary>Price per litre, worked out for the driver to check the receipt.</summary>
    public string UnitPriceText => TryParseDecimal(AmountText, out var amount) && TryParseDecimal(LitresText, out var litres) && litres > 0
        ? $"{amount / litres:0.000} {Currency}/l"
        : string.Empty;

    public string PaymentMethod
    {
        get => paymentMethod;
        set
        {
            if (SetProperty(ref paymentMethod, value))
            {
                RaisePropertyChanged(nameof(ShowCardLabel));
            }
        }
    }

    public bool ShowCardLabel => PaymentMethod != "Gotovina";

    public string CardLabel
    {
        get => cardLabel;
        set => SetProperty(ref cardLabel, value);
    }

    public string OdometerText
    {
        get => odometerText;
        set => SetProperty(ref odometerText, value);
    }

    public string Location
    {
        get => location;
        set => SetProperty(ref location, value);
    }

    public string Notes
    {
        get => notes;
        set => SetProperty(ref notes, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaisePropertyChanged(nameof(SubmitText));
                MainThread.BeginInvokeOnMainThread(() => (SubmitCommand as Command)?.ChangeCanExecute());
            }
        }
    }

    public string SubmitText => IsBusy ? "Slanje…" : "Pošalji trošak";

    public bool HasReceipt => receiptBytes is { Length: > 0 };

    public bool HasNoReceipt => !HasReceipt;

    public bool HasReceiptPreview => receiptPreview is not null;

    public ImageSource? ReceiptPreview => receiptPreview;

    public string ReceiptText => receiptBytes is { Length: > 0 } bytes
        ? $"{receiptFileName} · {ByteSize.Format(bytes.Length)}"
        : string.Empty;

    public int PendingCount => queue.PendingCount;

    public bool HasPending => PendingCount > 0;

    public string PendingText => $"Čeka slanje: {PendingCount} (bez veze sa serverom)";

    /// <summary>The page opens: the trip from the phone, currency of the next stop's country.</summary>
    public void Prepare()
    {
        portal = session.CurrentSession?.User.Id is { } userId ? portalStore.Load(userId)?.Portal : null;
        var country = portal?.Stops?.FirstOrDefault(stop => stop.Id == portal.NextStopId)?.CountryCode
                      ?? portal?.Stops?.LastOrDefault()?.CountryCode;
        Currency = country is not null && CountryCurrency.TryGetValue(country, out var local)
            ? local
            : Preferences.Default.Get(LastCurrencyKey, "EUR");
        RaisePropertyChanged(nameof(TripText));
        RaisePropertyChanged(nameof(PendingCount));
        RaisePropertyChanged(nameof(HasPending));
        RaisePropertyChanged(nameof(PendingText));
    }

    private void SelectCategory(ExpenseCategoryOption? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var item in Categories)
        {
            item.IsSelected = ReferenceEquals(item, option);
        }

        RaisePropertyChanged(nameof(SelectedCategory));
        RaisePropertyChanged(nameof(ShowLitres));
    }

    private void UseMyLocation()
    {
        if (gpsTracker.LastLocation is { } fix)
        {
            Location = string.Create(CultureInfo.InvariantCulture, $"{fix.Latitude:0.00000}, {fix.Longitude:0.00000}");
        }
        else
        {
            SafeUi.Run(() => notifier.ShowInfoAsync("GPS lokacija još nije poznata. Upišite mesto."));
        }
    }

    /// <summary>A photo or file from the page (camera, gallery, PDF); null removes it.</summary>
    public void SetReceipt(byte[]? bytes, string? fileName, string? contentType)
    {
        if (bytes is { Length: > MaxReceiptBytes })
        {
            ErrorMessage = $"Račun je prevelik ({ByteSize.Format(bytes.Length)}). Najviše 10 MB.";
            return;
        }

        receiptBytes = bytes;
        receiptFileName = fileName;
        receiptContentType = contentType;
        receiptPreview = bytes is { Length: > 0 } && contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
            ? ImageSource.FromStream(() => new MemoryStream(bytes))
            : null;
        ErrorMessage = null;
        foreach (var name in new[] { nameof(HasReceipt), nameof(HasNoReceipt), nameof(HasReceiptPreview), nameof(ReceiptPreview), nameof(ReceiptText) })
        {
            RaisePropertyChanged(name);
        }
    }

    /// <summary>True when the expense was saved (sent now or queued); the page then closes.</summary>
    public event EventHandler? Saved;

    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        if (!TryParseDecimal(AmountText, out var amount) || amount <= 0)
        {
            ErrorMessage = "Unesite iznos veći od nule.";
            return;
        }

        decimal? litres = null;
        if (ShowLitres && !string.IsNullOrWhiteSpace(LitresText))
        {
            if (!TryParseDecimal(LitresText, out var parsed) || parsed <= 0)
            {
                ErrorMessage = "Količina litara nije ispravna.";
                return;
            }

            litres = parsed;
        }

        int? odometer = null;
        if (!string.IsNullOrWhiteSpace(OdometerText))
        {
            if (!int.TryParse(OdometerText.Replace(".", string.Empty).Replace(" ", string.Empty), out var km) || km <= 0)
            {
                ErrorMessage = "Kilometraža nije ispravna.";
                return;
            }

            odometer = km;
        }

        var trip = portal?.Trip;
        var draft = new DriverExpenseDraft(
            SelectedCategory.Code,
            trip?.Id,
            amount,
            DateTimeOffset.Now,
            Clean(Location),
            portal?.Stops?.FirstOrDefault(stop => stop.Id == portal.NextStopId)?.CountryCode,
            PaymentMethod switch
            {
                "Gotovina" => "cash",
                "Lična kartica vozača" => "driver-card",
                "Fuel kartica" => "fuel-card",
                _ => "company-card",
            },
            ShowCardLabel ? Clean(CardLabel) : null,
            litres,
            litres is > 0 ? Math.Round(amount / litres.Value, 3) : null,
            odometer,
            trip?.VehicleId ?? portal?.Driver?.CurrentVehicleId,
            Clean(Notes),
            receiptBytes,
            receiptFileName,
            receiptContentType,
            Currency);

        try
        {
            IsBusy = true;
            Preferences.Default.Set(LastCurrencyKey, Currency);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var sent = await queue.SubmitAsync(draft, timeout.Token).ConfigureAwait(false);
            await (sent
                ? notifier.ShowSuccessAsync("Trošak je poslat dispečeru na proveru.")
                : notifier.ShowInfoAsync("Nema veze: trošak je sačuvan i biće poslat kad se veza vrati.")).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => Saved?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            ErrorMessage = ApiClient.DescribeError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // "12,50" and "12.50" both mean 12.5 (Serbian keyboards type a comma).
    private static bool TryParseDecimal(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
