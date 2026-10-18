using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>A driver document with its expiry (licence, CPC, ADR, tachograph card, medical).</summary>
public sealed record DocumentItem(string Title, string Detail, string StatusText, bool IsExpired, bool IsExpiringSoon);

/// <summary>
/// "Profil": the driver, documents with expiry warnings (30 days ahead), the assigned
/// vehicle and its dimensions, trips done, and the account (settings, sign out).
/// </summary>
public sealed class ProfileViewModel : ViewModelBase
{
    private const string CacheKey = "profile";
    private static readonly CultureInfo Sr = CultureInfo.GetCultureInfo("sr-Latn-RS");
    private readonly ApiClient apiClient;
    private readonly MobileSessionService session;
    private readonly DriverDataCache cache;
    private readonly GpsTelemetryUploader telemetry;
    private readonly DriverPortalStore portalStore;
    private readonly IMobileNotifier notifier;
    private DriverProfile? profile;
    private string statusText = string.Empty;
    private bool isBusy;
    private bool isPullRefreshing;
    private bool isNotDriver;

    public ProfileViewModel(ApiClient apiClient, MobileSessionService session, DriverDataCache cache, GpsTelemetryUploader telemetry, DriverPortalStore portalStore, IMobileNotifier notifier)
    {
        this.apiClient = apiClient;
        this.session = session;
        this.cache = cache;
        this.telemetry = telemetry;
        this.portalStore = portalStore;
        this.notifier = notifier;
        RefreshCommand = new Command(async () =>
        {
            await LoadAsync();
            IsPullRefreshing = false;
        });
        SignInCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Login));
        OpenSettingsCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Settings));
        OpenMapsCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.InstallMaps));
    }

    public ObservableCollection<DocumentItem> Documents { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SignInCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenMapsCommand { get; }

    public bool IsSignedIn => session.IsSignedIn;

    public bool ShowSignIn => !session.IsSignedIn;

    public bool HasProfile => profile is not null;

    public bool ShowNotDriver => session.IsSignedIn && isNotDriver;

    public string Name => profile?.FullName ?? session.CurrentSession?.User.DisplayName ?? "Niste prijavljeni";

    public string Initials => Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2)
        .Aggregate(string.Empty, (text, part) => text + char.ToUpperInvariant(part[0]));

    public string Subtitle => profile is null
        ? session.CurrentSession?.User.Email ?? string.Empty
        : string.Join(" · ", new[] { profile.EmployeeNumber is { Length: > 0 } number ? $"br. {number}" : null, DriverStatus(profile.Status), profile.Phone }
            .Where(part => !string.IsNullOrEmpty(part)));

    public string CompletedTripsText => profile?.CompletedTrips.ToString(Sr) ?? "—";

    public string CompletedThisMonthText => profile?.CompletedTripsThisMonth.ToString(Sr) ?? "—";

    public string DocumentWarningsText
    {
        get
        {
            var expired = Documents.Count(item => item.IsExpired);
            var soon = Documents.Count(item => item.IsExpiringSoon);
            return expired > 0 ? $"Isteklo: {expired}" : soon > 0 ? $"Ističe uskoro: {soon}" : "Sve važi";
        }
    }

    public bool HasDocumentProblem => Documents.Any(item => item.IsExpired || item.IsExpiringSoon);

    public bool HasVehicle => profile?.Vehicle is not null;

    public string VehicleTitle => profile?.Vehicle is { } vehicle
        ? $"{vehicle.Registration} · {string.Join(' ', new[] { vehicle.Make, vehicle.Model }.Where(part => !string.IsNullOrWhiteSpace(part)))}".TrimEnd(' ', '·')
        : string.Empty;

    public string VehicleDetail => profile?.Vehicle is { } vehicle
        ? string.Create(Sr, $"{vehicle.GrossWeightTons:0.#} t · visina {vehicle.HeightMeters:0.00} m · širina {vehicle.WidthMeters:0.00} m · dužina {vehicle.LengthMeters:0.0} m · {vehicle.Axles} osovina")
        : string.Empty;

    public string AccountText => session.CurrentSession?.User.Email ?? string.Empty;

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public bool IsPullRefreshing
    {
        get => isPullRefreshing;
        set => SetProperty(ref isPullRefreshing, value);
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await session.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
            if (session.CurrentSession?.User.Id is not { } userId)
            {
                await ApplyAsync(null, string.Empty);
                return;
            }

            if (profile is null && cache.Load<DriverProfile>(CacheKey, userId) is { } saved)
            {
                await ApplyAsync(saved.Value, $"Sačuvano {saved.SavedAt:dd.MM. HH:mm}");
            }

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                if (await apiClient.GetDriverProfileAsync(timeout.Token).ConfigureAwait(false) is { } fresh)
                {
                    cache.Save(CacheKey, fresh, userId);
                    isNotDriver = false;
                    await ApplyAsync(fresh, $"Osveženo {DateTime.Now:HH:mm}");
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                isNotDriver = true;
                await ApplyAsync(null, string.Empty);
            }
            catch (Exception)
            {
                await MainThread.InvokeOnMainThreadAsync(() => StatusText = profile is null ? "Bez veze sa serverom." : $"{StatusText} · bez veze");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LogoutAsync()
    {
        await telemetry.StopAsync(clearQueue: true).ConfigureAwait(false);
        if (ChatClientAccessor.Current is { } chatClient)
        {
            await chatClient.StopAsync().ConfigureAwait(false);
        }
        portalStore.Clear();
        await session.LogoutAsync().ConfigureAwait(false);
        await notifier.ShowInfoAsync("Odjavljeni ste.").ConfigureAwait(false);
    }

    private Task ApplyAsync(DriverProfile? value, string status)
        => MainThread.InvokeOnMainThreadAsync(() =>
        {
            profile = value;
            Documents.Clear();
            if (value is not null)
            {
                foreach (var document in BuildDocuments(value))
                {
                    Documents.Add(document);
                }
            }

            StatusText = status;
            foreach (var name in new[]
                     {
                         nameof(IsSignedIn), nameof(ShowSignIn), nameof(HasProfile), nameof(ShowNotDriver), nameof(Name),
                         nameof(Initials), nameof(Subtitle), nameof(CompletedTripsText), nameof(CompletedThisMonthText),
                         nameof(DocumentWarningsText), nameof(HasDocumentProblem), nameof(HasVehicle), nameof(VehicleTitle),
                         nameof(VehicleDetail), nameof(AccountText),
                     })
            {
                RaisePropertyChanged(name);
            }
        });

    private static IEnumerable<DocumentItem> BuildDocuments(DriverProfile profile)
    {
        yield return Document("Vozačka dozvola", Join(profile.LicenseNumber, profile.LicenseCategories), profile.LicenseExpiry);
        yield return Document("CPC (Kod 95)", null, profile.Code95Expiry);
        yield return Document("Tahograf kartica", profile.TachographCardNumber, profile.TachographCardExpiry);
        yield return Document("Lekarsko uverenje", null, profile.MedicalExpiry);
        if (profile.AdrExpiry is not null)
        {
            yield return Document("ADR sertifikat", null, profile.AdrExpiry);
        }
    }

    private static DocumentItem Document(string title, string? detail, DateOnly? expiry)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (expiry is not { } date)
        {
            return new DocumentItem(title, detail ?? "Nema podatka o roku", "—", false, false);
        }

        var days = date.DayNumber - today.DayNumber;
        var status = days < 0 ? "Isteklo" : days <= 30 ? $"Za {days} d" : "Važi";
        return new DocumentItem(title, Join(detail, $"važi do {date.ToString("dd.MM.yyyy.", Sr)}") ?? string.Empty, status, days < 0, days is >= 0 and <= 30);
    }

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return text.Length == 0 ? null : text;
    }

    private static string? DriverStatus(string? status) => status switch
    {
        "Available" => "slobodan",
        "OnTrip" or "Driving" => "na turi",
        "Resting" or "OnBreak" => "odmor",
        "OffDuty" => "van smene",
        "Inactive" => "neaktivan",
        _ => status?.ToLowerInvariant(),
    };
}
