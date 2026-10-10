namespace ProMapCargo.Mobile.Services;

/// <summary>
/// A request from the home screen for the navigation screen, picked up when it
/// appears: open the route planner ("Kuda idemo?") or start a route to a place.
/// </summary>
public sealed record HomeNavigationRequest(bool OpenPlanner, RecentDestination? Destination);

public static class NavigationRequests
{
    private static HomeNavigationRequest? pending;

    public static void OpenPlanner() => pending = new HomeNavigationRequest(true, null);

    public static void RouteTo(RecentDestination destination) => pending = new HomeNavigationRequest(false, destination);

    /// <summary>Returns the pending request once and clears it.</summary>
    public static HomeNavigationRequest? Take()
    {
        var request = pending;
        pending = null;
        return request;
    }
}
