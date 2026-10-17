using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.viewModel = viewModel;
        EmailEntry.Completed += (_, _) => PasswordEntry.Focus();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.Refresh();
        viewModel.LoginSucceeded += HandleLoginSucceeded;
    }

    protected override void OnDisappearing()
    {
        viewModel.LoginSucceeded -= HandleLoginSucceeded;
        base.OnDisappearing();
    }

    private void HandleLoginSucceeded(object? sender, EventArgs e)
        => SafeUi.Run(() => AppNavigation.GoToAsync(AppNavigation.Dashboard));
}
