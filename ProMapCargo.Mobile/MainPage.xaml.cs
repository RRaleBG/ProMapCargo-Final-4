using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    // AppNavigation never throws: an unknown route shows a message instead of closing the app.
    private async void OnOpenOperationsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Dashboard);

    private async void OnOpenNavigationClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Navigation);

    private async void OnOpenMapsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.InstallMaps);

    private async void OnOpenSettingsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Settings);
}
