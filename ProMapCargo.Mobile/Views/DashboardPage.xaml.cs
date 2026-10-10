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
        if (!viewModel.IsBusy)
        {
            await viewModel.LoadAsync();
        }
    }

    private async void OnOpenSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//settings");
    }

    private async void OnOpenNavigationClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//dashboard/mobile-navigation");
    }

    private void OnQuickLinkClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var link = button.CommandParameter;
        if (viewModel.OpenLinkCommand.CanExecute(link))
        {
            viewModel.OpenLinkCommand.Execute(link);
        }
    }

    private void OnToggleDiagnosticsClicked(object? sender, EventArgs e)
    {
        DiagnosticsPanel.IsVisible = !DiagnosticsPanel.IsVisible;
        DiagnosticsToggle.Text = DiagnosticsPanel.IsVisible
            ? "Sakrij dijagnostiku rute"
            : "Prikaži dijagnostiku rute";
    }
}
