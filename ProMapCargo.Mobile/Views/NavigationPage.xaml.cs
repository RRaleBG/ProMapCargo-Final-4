using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class MobileNavigationPage : ContentPage
{
    private readonly NavigationViewModel viewModel;

    private const string NavigationUrl =
        "http://localhost:5090/Navigation?embedded=1&mobile=1";

    public MobileNavigationPage(NavigationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        this.viewModel = viewModel;

        BindingContext = this.viewModel;

        ConfigureWebView();
    }

    private void ConfigureWebView()
    {
        NavigationWebView.Navigating += OnWebViewNavigating;
        NavigationWebView.Navigated += OnWebViewNavigated;

        NavigationWebView.Source = NavigationUrl;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (NavigationWebView.Source is null)
        {
            NavigationWebView.Source = NavigationUrl;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Native GPS tracking belongs to the MAUI layer.
        // The actual navigation UI remains inside the WebView.
    }

    private async void OnWebViewNavigated(
        object? sender,
        WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
        {
            LoadingOverlay.IsVisible = false;

            return;
        }

        LoadingOverlay.IsVisible = false;

        await DisplayAlert("ProMap Cargo", "Navigacija trenutno nije dostupna.", "OK");
    }

    private void OnWebViewNavigating(object? sender,  WebNavigatingEventArgs e)
    {
        LoadingOverlay.IsVisible = true;
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
        {
            NavigationWebView.Navigating -= OnWebViewNavigating;
            NavigationWebView.Navigated -= OnWebViewNavigated;
        }

        base.OnHandlerChanging(args);
    }
}
