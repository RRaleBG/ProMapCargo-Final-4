using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly ApiClient apiClient;
    private readonly MobileSessionService sessionService;
    private bool isBusy;
    private string statusText = "Ready";
    private string routeSummary = "No route loaded.";

    public DashboardViewModel(ApiClient apiClient, MobileSessionService sessionService)
    {
        this.apiClient = apiClient;
        this.sessionService = sessionService;
        Vehicles = [];
        Drivers = [];
        Orders = [];
        Trips = [];
        RefreshCommand = new Command(async () => await LoadAsync(), () => !IsBusy);
    }

    public ObservableCollection<VehicleItem> Vehicles { get; }

    public ObservableCollection<DriverItem> Drivers { get; }

    public ObservableCollection<TransportOrderItem> Orders { get; }

    public ObservableCollection<TripItem> Trips { get; }

    public ICommand RefreshCommand { get; }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string RouteSummary
    {
        get => routeSummary;
        private set => SetProperty(ref routeSummary, value);
    }

    public string WelcomeText => sessionService.CurrentSession?.User.DisplayName is { Length: > 0 } displayName
        ? $"Welcome, {displayName}"
        : "Welcome";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value) && RefreshCommand is Command command)
            {
                command.ChangeCanExecute();
            }
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Loading operations data...";

            var vehicles = await apiClient.GetVehiclesAsync(CancellationToken.None).ConfigureAwait(false);
            var drivers = await apiClient.GetDriversAsync(CancellationToken.None).ConfigureAwait(false);
            var orders = await apiClient.GetOrdersAsync(CancellationToken.None).ConfigureAwait(false);
            var trips = await apiClient.GetTripsAsync(CancellationToken.None).ConfigureAwait(false);
            var route = await apiClient.GetRouteAsync(
                new RouteRequest(
                    new GeoPoint(44.8176, 20.4633),
                    new GeoPoint(45.2671, 19.8335)),
                CancellationToken.None).ConfigureAwait(false);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ReplaceItems(Vehicles, vehicles);
                ReplaceItems(Drivers, drivers);
                ReplaceItems(Orders, orders);
                ReplaceItems(Trips, trips);
                RaisePropertyChanged(nameof(WelcomeText));
            });

            RouteSummary = route?.Routes.FirstOrDefault() is { } selectedRoute
                ? $"Belgrade → Novi Sad · {selectedRoute.Distance / 1000:0.#} km · {selectedRoute.Duration / 60:0} min"
                : "Route service available but no route is currently selected.";

            StatusText = $"Synced {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
