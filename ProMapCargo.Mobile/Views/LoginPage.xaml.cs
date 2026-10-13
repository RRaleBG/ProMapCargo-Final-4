using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoginSucceeded += HandleLoginSucceeded;
    }

    protected override void OnDisappearing()
    {
        viewModel.LoginSucceeded -= HandleLoginSucceeded;
        base.OnDisappearing();
    }

    private async void HandleLoginSucceeded(object? sender, EventArgs e)
        => await AppNavigation.GoToAsync(AppNavigation.Dashboard);
}
