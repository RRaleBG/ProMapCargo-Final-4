using Microsoft.Maui.ApplicationModel;
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

            await shell.GoToAsync("//login");
        });
    }
}
