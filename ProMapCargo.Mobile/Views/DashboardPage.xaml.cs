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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ReloadRecents();

        if (!viewModel.IsBusy)
        {
            await viewModel.LoadAsync();
        }
    }

    private async void OnOpenSettingsClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Settings);

    private async void OnOpenNavigationClicked(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Navigation);

    // "Kuda idemo?": open navigation with the route planner already showing.
    private async void OnSearchTapped(object? sender, TappedEventArgs e)
    {
        NavigationRequests.OpenPlanner();
        await AppNavigation.GoToAsync(AppNavigation.Navigation);
    }

    // A recent destination: route there from the current position right away.
    private async void OnRecentTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not RecentDestination destination)
        {
            return;
        }

        NavigationRequests.RouteTo(destination);
        await AppNavigation.GoToAsync(AppNavigation.Navigation);
    }

    private async void OnTileNavigationTapped(object? sender, TappedEventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Navigation);

    private async void OnTileMapsTapped(object? sender, TappedEventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.InstallMaps);

    private async void OnTileSettingsTapped(object? sender, TappedEventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Settings);

    // Dispatch has no mobile screen yet; the view model tells the driver it is on the web portal.
    private void OnTileDispatchTapped(object? sender, TappedEventArgs e)
    {
        var link = viewModel.QuickLinks.FirstOrDefault(item => item.Title == "Dispečing");
        if (link is not null && viewModel.OpenLinkCommand.CanExecute(link))
        {
            viewModel.OpenLinkCommand.Execute(link);
        }
    }
}
