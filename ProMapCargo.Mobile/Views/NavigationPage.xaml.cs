using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class MobileNavigationPage : ContentPage
{
    private readonly NavigationViewModel viewModel;
    private bool _isTracking;
    private IDispatcherTimer _locationTimer;
    private const string NavigationUrl = "http://localhost:5090/Navigation?embedded=1&mobile=1";

    public MobileNavigationPage()
    {
        InitializeComponent();

        // 1. Podesi timer koji će na svaku sekundu čitati GPS i slati u Web
        _locationTimer = Dispatcher.CreateTimer();
        _locationTimer.Interval = TimeSpan.FromSeconds(1);
        _locationTimer.Tick += async (s, e) => await SendLocationToWebAsync();

        // 2. Skloni loading ekran kada se web stranica učita
        NavigationWebView.Navigated += (s, e) =>
        {
            LoadingOverlay.IsVisible = false;
            // Ovde počinjemo praćenje tek kad je mapa spremna
            _isTracking = true;
            _locationTimer.Start();
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Zatraži dozvole od korisnika pri otvaranju stranice
        var status = await CheckAndRequestLocationPermission();
        if (status == PermissionStatus.Granted)
        {
            // Učitaj svoju web aplikaciju (Zameni URL sa svojim lokalnim ili produkcionim)
            NavigationWebView.Source = "https://app.promapcargo.com/navigation-module";
        }
        else
        {
            await DisplayAlert("Greška", "Za navigaciju je neophodan pristup GPS-u.", "OK");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Zaustavi GPS kada korisnik izađe sa stranice da štediš bateriju
        _isTracking = false;
        _locationTimer.Stop();
    }

    // --- METODA ZA ČITANJE GPS-a I SLANJE U WEB ---
    private async Task SendLocationToWebAsync()
    {
        if (!_isTracking) return;

        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(2));
            var location = await Geolocation.Default.GetLocationAsync(request);

            if (location != null)
            {
                var lat = location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var lng = location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var heading = location.Course?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0";
                var speed = (location.Speed * 3.6)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0";

                // Proveravamo da li funkcija postoji u JS okruženju pre nego što je pozovemo
                var jsCode = $@"
                if (typeof window.updateTruckLocation === 'function') {{
                    window.updateTruckLocation({lat}, {lng}, {heading}, {speed});
                }}
            ";

                await NavigationWebView.EvaluateJavaScriptAsync(jsCode);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPS Greška: {ex.Message}");
        }
    }



    // --- METODA ZA DOZVOLE ---
    private async Task<PermissionStatus> CheckAndRequestLocationPermission()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

        if (status == PermissionStatus.Granted)
            return status;

        if (Permissions.ShouldShowRationale<Permissions.LocationWhenInUse>())
        {
            await DisplayAlert("GPS", "Morate odobriti lokaciju kako bi navigacija radila.", "OK");
        }

        status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return status;
    }
}