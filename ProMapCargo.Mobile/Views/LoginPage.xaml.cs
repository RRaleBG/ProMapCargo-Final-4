using Microsoft.Maui.Controls;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.LoginSucceeded += HandleLoginSucceeded;
    }

    private async void HandleLoginSucceeded(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//dashboard");
    }
}
