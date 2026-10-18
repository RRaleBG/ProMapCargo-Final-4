using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel viewModel;

    public ProfilePage(ProfileViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SafeUi.Run(() => viewModel.LoadAsync());
    }

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
