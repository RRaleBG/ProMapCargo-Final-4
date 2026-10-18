using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class ExpensesPage : ContentPage
{
    private readonly ExpensesViewModel viewModel;

    public ExpensesPage(ExpensesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SafeUi.Run(() => viewModel.RefreshAsync());
    }

    private void OnBackClicked(object? sender, EventArgs e) => SafeUi.Run(() => Shell.Current.GoToAsync(".."));

    private void OnReceiptClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ExpenseListItem { ReceiptFileId: { } fileId })
        {
            SafeUi.Run(() => viewModel.OpenReceiptAsync(fileId));
        }
    }
}
