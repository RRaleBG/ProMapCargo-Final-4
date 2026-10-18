using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.Views;

namespace ProMapCargo.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(AppNavigation.TripDetails, typeof(TripDetailsPage));
        Routing.RegisterRoute(AppNavigation.Expense, typeof(ExpensePage));
        Routing.RegisterRoute(AppNavigation.Expenses, typeof(ExpensesPage));
    }
}
