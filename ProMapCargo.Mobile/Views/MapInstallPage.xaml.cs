using ProMapCargo.Mobile.Models;
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ContinueRequested += HandleContinueRequested;
        await viewModel.LoadAsync(CancellationToken.None);
    }

    protected override void OnDisappearing()
    {
        viewModel.ContinueRequested -= HandleContinueRequested;
        base.OnDisappearing();
    }

    private async void HandleContinueRequested(object? sender, EventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var shell = Shell.Current;
            if (shell is null)
            {
                return;
            }

            await shell.GoToAsync("//dashboard");
        });
    }

    private void OnInstallClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: MapPackageCatalogItem package })
        {
            return;
        }

        if (viewModel.InstallCommand.CanExecute(package))
        {
            viewModel.InstallCommand.Execute(package);
        }
    }

    private void OnInstallBundleClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: OfflineRoutingBundleCatalogItem bundle })
        {
            return;
        }

        if (viewModel.InstallBundleCommand.CanExecute(bundle))
        {
            viewModel.InstallBundleCommand.Execute(bundle);
        }
    }
}
