using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class MapInstallPage : ContentPage
{
    private readonly MapInstallViewModel viewModel;

    public MapInstallPage(MapInstallViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SafeUi.Run(() => viewModel.LoadAsync(CancellationToken.None));
    }

    private void OnInstallClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: MapPackageCatalogItem package } &&
            viewModel.InstallCommand.CanExecute(package))
        {
            viewModel.InstallCommand.Execute(package);
        }
    }

    private void OnInstallBundleClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: OfflineRoutingBundleCatalogItem bundle } &&
            viewModel.InstallBundleCommand.CanExecute(bundle))
        {
            viewModel.InstallBundleCommand.Execute(bundle);
        }
    }

    // Deleting a region means downloading it again later: confirmed first.
    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not InstalledOfflineItem item)
        {
            return;
        }

        SafeUi.Run(async () =>
        {
            var what = item.IsRoutingPackage ? "Rute bez signala" : "Mapa bez signala";
            if (await DisplayAlertAsync($"Obrisati „{item.Title}“?", $"{what} za ovaj region više neće raditi dok se ponovo ne preuzme.", "Obriši", "Otkaži"))
            {
                await viewModel.DeleteAsync(item);
            }
        });
    }
}
