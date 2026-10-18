using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.Refresh();
    }

    private void OnReportProblemClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        var text = await DisplayPromptAsync("Prijavi problem", "Opišite šta ne radi. Administrator dobija i podatke o telefonu.", "Pošalji", "Otkaži", maxLength: 1000);
        if (!string.IsNullOrWhiteSpace(text))
        {
            await viewModel.ReportProblemAsync(text.Trim());
        }
    });

    // Signing out stops the position for dispatch and removes the saved trip: confirmed first.
    private void OnLogoutClicked(object? sender, EventArgs e) => SafeUi.Run(async () =>
    {
        if (await DisplayAlertAsync("Odjava", "Tura se više neće prikazivati, a lokacija se više ne šalje dispečeru.", "Odjavi se", "Otkaži"))
        {
            // App.xaml.cs opens sign-in when the session ends.
            await viewModel.LogoutAsync();
        }
    });
}
