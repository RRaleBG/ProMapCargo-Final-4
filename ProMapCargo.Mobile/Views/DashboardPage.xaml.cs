using ProMapCargo.Mobile.Services;
using Microsoft.Maui.Controls;
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

    private async void OnOpenSettingsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Settings);

    private async void OnOpenNavigationClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Navigation);

    // "Kuda idemo?": open navigation with the route planner already showing.
    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        NavigationRequests.OpenPlanner();
        await AppNavigation.GoToAsync(AppNavigation.Navigation);
    }

    // A recent destination: route there from the current position right away.
    private async void OnRecentClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not RecentDestination destination)
        {
            return;
        }

        NavigationRequests.RouteTo(destination);
        await AppNavigation.GoToAsync(AppNavigation.Navigation);
    }

    private async void OnTileNavigationClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Navigation);

    private async void OnTileMapsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.InstallMaps);

    private void OnTruckBansClicked(object? sender, EventArgs e)
        => SafeUi.Run(() => DisplayAlertAsync("Zabrane vožnje kamiona", viewModel.TruckBansDetails, "U redu"));

    private async void OnTileSettingsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Settings);

    // Dispatch has no mobile screen yet; the view model tells the driver it is on the web portal.
    private void OnTileDispatchClicked(object? sender, EventArgs e)
    {
        var link = viewModel.QuickLinks.FirstOrDefault(item => item.Title == "Dispečing");
        if (link is not null && viewModel.OpenLinkCommand.CanExecute(link))
        {
            viewModel.OpenLinkCommand.Execute(link);
        }
    }
}
