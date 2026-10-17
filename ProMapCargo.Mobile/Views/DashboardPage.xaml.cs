using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ReloadRecents();

        if (!viewModel.IsBusy)
        {
            SafeUi.Run(() => viewModel.LoadAsync());
        }
    }

    private void OnOpenSettingsClicked(object? sender, EventArgs e)
        => SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Settings));

    // "Kuda idemo?": open navigation with the route planner already showing.
    private void OnSearchClicked(object? sender, EventArgs e)
    {
        NavigationRequests.OpenPlanner();
        SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Navigation));
    }

    // A recent destination: route there from the current position right away.
    private void OnRecentClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not RecentDestination destination)
        {
            return;
        }

        NavigationRequests.RouteTo(destination);
        SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Navigation));
    }

    private void OnTileNavigationClicked(object? sender, EventArgs e)
        => SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Navigation));

    private void OnTileMapsClicked(object? sender, EventArgs e)
        => SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.InstallMaps));

    private void OnTruckBansClicked(object? sender, EventArgs e)
        => SafeUi.Run(() => DisplayAlertAsync("Zabrane vožnje kamiona", viewModel.TruckBansDetails, "U redu"));

    // ---- Trip actions: confirmed first, a tap by mistake while driving must not count ----

    private void OnNextStopActionClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        if (await DisplayAlertAsync("Stanica", viewModel.NextStopConfirmText, viewModel.NextStopActionText, "Otkaži"))
        {
            await viewModel.NextStopActionAsync();
        }
    });

    private void OnStartTripClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        if (await DisplayAlertAsync("Započni turu", $"{viewModel.TripTitle}: {viewModel.TripRoute}", "Započni", "Otkaži"))
        {
            await viewModel.StartTripAsync();
        }
    });

    private void OnPauseClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        var title = viewModel.IsTripPaused ? "Nastaviti vožnju?" : "Započeti pauzu?";
        if (await DisplayAlertAsync(title, "Dispečer vidi promenu odmah.", viewModel.PauseButtonText, "Otkaži"))
        {
            await viewModel.TogglePauseAsync();
        }
    });

    private void OnIncidentClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        const string critical = "Hitno (kvar, nezgoda)";
        const string warning = "Upozorenje (kašnjenje, gužva)";
        var kind = await DisplayActionSheetAsync("Prijava incidenta", "Otkaži", null, critical, warning);
        if (kind is not (critical or warning))
        {
            return;
        }

        var message = await DisplayPromptAsync("Prijava incidenta", "Kratko opišite šta se desilo.", "Pošalji", "Otkaži", maxLength: 500);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await viewModel.ReportIncidentAsync(message.Trim(), kind == critical);
    });

    private void OnAcceptRouteClicked(object? sender, EventArgs e)
        => SafeUi.Run(() => viewModel.AcceptRouteAsync());

    private void OnRejectRouteClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        var reason = await DisplayPromptAsync("Odbijanje rute", "Razlog (nije obavezno):", "Odbij rutu", "Otkaži", maxLength: 300);
        if (reason is null)
        {
            return; // cancelled
        }

        await viewModel.RejectRouteAsync(string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    });

    private void OnAddExpenseClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        var category = await DisplayActionSheetAsync("Kategorija troška", "Otkaži", null, "Gorivo", "Putarina", "Parking", "Hrana", "Smeštaj", "Ostalo");
        if (string.IsNullOrWhiteSpace(category) || category == "Otkaži")
        {
            return;
        }

        var amountText = await DisplayPromptAsync("Iznos troška", "Unesite iznos u EUR.", "Dalje", "Otkaži", keyboard: Keyboard.Numeric, maxLength: 12);
        if (string.IsNullOrWhiteSpace(amountText) || !decimal.TryParse(amountText.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            return;
        }

        var payment = await DisplayActionSheetAsync("Način plaćanja", "Otkaži", null, "Gotovina", "Lična kartica vozača", "Službena kartica", "Fuel kartica");
        if (string.IsNullOrWhiteSpace(payment) || payment == "Otkaži")
        {
            return;
        }

        string? cardLabel = null;
        if (payment != "Gotovina")
        {
            cardLabel = await DisplayPromptAsync("Kartica", "Unesite naziv ili poslednje cifre kartice.", "Sačuvaj", "Preskoči", maxLength: 80);
        }

        var location = await DisplayPromptAsync("Lokacija", "Gde je trošak nastao?", "Dalje", "Preskoči", maxLength: 120);
        var notes = await DisplayPromptAsync("Napomena", "Objasnite trošak za dispečera.", "Dalje", "Preskoči", maxLength: 400);

        FileResult? receipt = null;
        if (await DisplayAlertAsync("Račun", "Da li želite da dodate fotografiju ili PDF računa?", "Dodaj", "Preskoči"))
        {
            var source = await DisplayActionSheetAsync("Izvor računa", "Otkaži", null, "Kamera", "Galerija/PDF");
            try
            {
                receipt = source switch
                {
                    "Kamera" => await MediaPicker.Default.CapturePhotoAsync(),
                    "Galerija/PDF" => await FilePicker.Default.PickAsync(new PickOptions
                    {
                        PickerTitle = "Izaberite račun",
                        FileTypes = FilePickerFileType.Images
                    }),
                    _ => null
                };
            }
            catch
            {
                receipt = null;
            }
        }

        byte[]? receiptBytes = null;
        string? receiptName = null;
        string? receiptContentType = null;
        if (receipt is not null)
        {
            await using var receiptStream = await receipt.OpenReadAsync();
            using var memory = new MemoryStream();
            await receiptStream.CopyToAsync(memory);
            receiptBytes = memory.ToArray();
            receiptName = receipt.FileName;
            receiptContentType = receipt.ContentType;
        }

        await viewModel.SubmitExpenseAsync(
            category switch
            {
                "Gorivo" => "Fuel",
                "Putarina" => "Toll",
                "Parking" => "Parking",
                "Hrana" => "Food",
                "Smeštaj" => "Accommodation",
                _ => "Other"
            },
            amount,
            payment switch
            {
                "Gotovina" => "cash",
                "Lična kartica vozača" => "driver-card",
                "Službena kartica" => "company-card",
                _ => "fuel-card"
            },
            string.IsNullOrWhiteSpace(cardLabel) ? null : cardLabel.Trim(),
            string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            receiptBytes,
            receiptName,
            receiptContentType);
    });
}
