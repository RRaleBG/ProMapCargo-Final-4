using System.Text.Json;
using System.Text.Json.Serialization;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>
/// The state the navigation screen sends to its web map (window.proMapMobile.applyState):
/// position with heading and speed from consecutive fixes, and the route only when it changed.
/// </summary>
public sealed class NavigationMapState
{
    private static readonly TimeSpan MinPushInterval = TimeSpan.FromMilliseconds(250);

    private DateTimeOffset lastPushAt = DateTimeOffset.MinValue;
    // Route last delivered to the web map; empty forces a full resend (page load).
    private string lastSentRouteKey = string.Empty;
    private WebPoint? lastLocation;
    private DateTimeOffset? lastLocationAt;

    public sealed record Update(string Json, string RouteKey, bool IncludesRoute);

    public void ResetLocation()
    {
        lastLocation = null;
        lastLocationAt = null;
    }

    public void ForceRouteResend() => lastSentRouteKey = string.Empty;

    /// <summary>The next payload, or null when an unforced update comes too soon after the last one.</summary>
    public Update? Next(NavigationViewModel viewModel, bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - lastPushAt < MinPushInterval)
        {
            return null;
        }

        lastPushAt = now;

        var current = ToWebPoint(viewModel.CurrentLocation);
        var motion = CalculateMotion(current);

        var points = viewModel.RoutePolylinePoints;
        var routeKey = points.Count == 0
            ? ""
            : $"{points.Count}:{points[0].Lat},{points[0].Lon}:{points[^1].Lat},{points[^1].Lon}:{viewModel.RouteManeuvers.Count}";
        var includeRoute = routeKey != lastSentRouteKey;

        var payload = new
        {
            mobile = true,
            live = true,
            follow = true,
            start = ToWebPoint(viewModel.RouteStartPoint),
            destination = ToWebPoint(viewModel.RouteDestinationPoint),
            current,
            heading = motion.Heading,
            // Same filtered speed the bubble shows; drives the speed-dependent zoom.
            speedKph = double.TryParse(viewModel.CurrentSpeedText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var shownSpeed)
                ? shownSpeed
                : motion.SpeedKph,
            // Route and maneuvers (thousands of points) only when they changed; the
            // map keeps the last ones. Sending them on every GPS fix made it stutter.
            route = includeRoute
                ? viewModel.RoutePolylinePoints
                    .Select(point => new
                    {
                        latitude = point.Lat,
                        longitude = point.Lon
                    })
                    .ToArray()
                : null,
            maneuvers = includeRoute
                ? viewModel.RouteManeuvers
                    .Select(maneuver => new
                    {
                        type = maneuver.Type,
                        instruction = maneuver.Instruction,
                        distanceMeters = maneuver.DistanceFromRouteStartMeters,
                        latitude = maneuver.Latitude,
                        longitude = maneuver.Longitude
                    })
                    .ToArray()
                : null,
            restrictionCount = viewModel.RouteViolations.Count,
            // NextManeuverIndex is 1-based; the web map marks the upcoming turn.
            nextManeuverIndex = Math.Max(0, viewModel.NextManeuverIndex - 1)
        };

        return new Update(JsonSerializer.Serialize(payload), routeKey, includeRoute);
    }

    /// <summary>The web map accepted the update.</summary>
    public void Delivered(Update update)
    {
        if (update.IncludesRoute)
        {
            lastSentRouteKey = update.RouteKey;
        }
    }

    private MotionSnapshot CalculateMotion(WebPoint? current)
    {
        if (current is null)
        {
            return new MotionSnapshot(null, null);
        }

        if (lastLocation is null || lastLocationAt is null)
        {
            lastLocation = current;
            lastLocationAt = DateTimeOffset.UtcNow;
            return new MotionSnapshot(null, null);
        }

        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = (now - lastLocationAt.Value).TotalSeconds;
        var distanceMeters = HaversineMeters(
            lastLocation.Latitude,
            lastLocation.Longitude,
            current.Latitude,
            current.Longitude);

        double? heading = null;
        if (distanceMeters >= 2)
        {
            heading = InitialBearing(
                lastLocation.Latitude,
                lastLocation.Longitude,
                current.Latitude,
                current.Longitude);
        }

        double? speedKph = null;
        if (elapsedSeconds > 0.5 && distanceMeters >= 1)
        {
            speedKph = distanceMeters / elapsedSeconds * 3.6;
        }

        lastLocation = current;
        lastLocationAt = now;

        return new MotionSnapshot(heading, speedKph);
    }

    private static double HaversineMeters(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        const double earthRadius = 6371000;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(DegreesToRadians(lat1)) *
            Math.Cos(DegreesToRadians(lat2)) *
            Math.Sin(dLon / 2) *
            Math.Sin(dLon / 2);

        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double InitialBearing(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        var firstLatitude = DegreesToRadians(lat1);
        var secondLatitude = DegreesToRadians(lat2);
        var deltaLongitude = DegreesToRadians(lon2 - lon1);

        var y = Math.Sin(deltaLongitude) * Math.Cos(secondLatitude);
        var x =
            Math.Cos(firstLatitude) * Math.Sin(secondLatitude) -
            Math.Sin(firstLatitude) *
            Math.Cos(secondLatitude) *
            Math.Cos(deltaLongitude);

        return (RadiansToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;

    private static double RadiansToDegrees(double value) => value * 180 / Math.PI;

    private static WebPoint? ToWebPoint(GeoPoint? point)
    {
        return point is null
            ? null
            : new WebPoint(point.Lat, point.Lon);
    }

    private sealed record MotionSnapshot(double? Heading, double? SpeedKph);

    private sealed record WebPoint(
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude);
}
