using System.Globalization;
using System.Text.Json.Serialization;
using ProMapCargo.Mobile.Models;
using ProMapCargo.OfflineRouting;

namespace ProMapCargo.Mobile.Services;

/// <summary>Truck dimensions sent with a route request (server TruckProfile JSON names).</summary>
public sealed record TruckProfileRequest(
    [property: JsonPropertyName("grossWeightT")] decimal GrossWeightTons,
    [property: JsonPropertyName("heightM")] decimal HeightMeters,
    [property: JsonPropertyName("widthM")] decimal WidthMeters,
    [property: JsonPropertyName("lengthM")] decimal LengthMeters,
    [property: JsonPropertyName("axleLoadT")] decimal? AxleLoadTons,
    [property: JsonPropertyName("axles")] int Axles);

/// <summary>
/// The truck the route is planned for: the vehicle of the driver's trip (height, width,
/// length, weight from the fleet record), else the default 40 t / 4.0 m truck. A bridge or a
/// weight limit is then judged against the real vehicle, online and offline.
/// </summary>
public sealed class TruckProfileProvider(DriverPortalStore portalStore, MobileSessionService session)
{
    /// <summary>Speed limit of a truck without a fleet record (the fleet default).</summary>
    public const double DefaultMaxSpeedKmh = 90;

    public sealed record Current(TruckProfileRequest? Request, TruckDimensions Dimensions, string Text, string Badge, double MaxSpeedKmh = DefaultMaxSpeedKmh);

    public Current Get()
    {
        var portal = session.CurrentSession?.User.Id is { } userId ? portalStore.Load(userId)?.Portal : null;
        if (portal?.Trip?.TruckProfile is not { } truck)
        {
            var fallback = OfflineRoutingEngine.DefaultTruck;
            return new Current(null, fallback, Describe(fallback.WeightTons, fallback.HeightMeters, fallback.WidthMeters, fallback.LengthMeters), "Podrazumevani");
        }

        var request = new TruckProfileRequest(truck.GrossWeightTons, truck.HeightMeters, truck.WidthMeters, truck.LengthMeters, truck.AxleLoadTons, truck.Axles);
        var dimensions = new TruckDimensions((double)truck.HeightMeters, (double)truck.WidthMeters, (double)truck.LengthMeters, (double)truck.GrossWeightTons);
        return new Current(
            request,
            dimensions,
            Describe(dimensions.WeightTons, dimensions.HeightMeters, dimensions.WidthMeters, dimensions.LengthMeters),
            portal.Trip.Vehicle is { Length: > 0 } registration ? registration : "Vozilo ture",
            truck.MaxSpeedKmh is > 0 ? (double)truck.MaxSpeedKmh.Value : DefaultMaxSpeedKmh);
    }

    private static string Describe(double tons, double height, double width, double length)
    {
        var sr = CultureInfo.GetCultureInfo("sr-Latn-RS");
        return string.Create(sr, $"{tons:0.#} t · {height:0.00} m · {width:0.00} m · {length:0.0} m");
    }
}
