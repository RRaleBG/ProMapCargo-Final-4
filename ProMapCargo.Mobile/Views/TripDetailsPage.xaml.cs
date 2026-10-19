using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class TripDetailsPage : ContentPage
{
    private readonly TripDetailsViewModel viewModel;

    public TripDetailsPage(TripDetailsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AppLifecycle.Resumed += OnAppResumed;
        SafeUi.Run(() => viewModel.LoadAsync());
    }

    protected override void OnDisappearing()
    {
        AppLifecycle.Resumed -= OnAppResumed;
        base.OnDisappearing();
    }

    // Back in the app: costs, stops and the advance may have changed meanwhile.
    private void OnAppResumed(object? sender, EventArgs e)
        => SafeUi.Run(() => viewModel.LoadAsync());

    private void OnBackClicked(object? sender, EventArgs e)
        => SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Dashboard));

    private void OnOpenNavigationClicked(object? sender, EventArgs e)
        => viewModel.OpenNavigationCommand.Execute(null);

    private void OnCallDispatchClicked(object? sender, EventArgs e)
        => viewModel.CallDispatchCommand.Execute(null);

    private void OnOpenReceiptClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is Guid receiptId)
        {
            viewModel.OpenReceiptCommand.Execute(receiptId);
        }
    }
}
