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

    // A region installs (or completes / updates) map and routes in one step.
    private void OnRegionClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is RegionPackage region)
        {
            SafeUi.Run(() => viewModel.InstallAsync(region));
        }
    }

    // Deleting a region means downloading it again later: confirmed first.
    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not InstalledRegion region)
        {
            return;
        }

        SafeUi.Run(async () =>
        {
            if (await DisplayAlertAsync($"Obrisati „{region.Title}“?", "Mapa i rute bez signala za ovaj region više neće raditi dok se ponovo ne preuzmu.", "Obriši", "Otkaži"))
            {
                await viewModel.DeleteAsync(region);
            }
        });
    }
}
