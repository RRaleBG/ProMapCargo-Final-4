using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

public sealed record DashboardSummary(
    [property: JsonPropertyName("activeTrips")] int ActiveTrips,
    [property: JsonPropertyName("activeVehicles")] int ActiveVehicles,
    [property: JsonPropertyName("activeDrivers")] int ActiveDrivers,
    [property: JsonPropertyName("openAlerts")] int OpenAlerts,
    [property: JsonPropertyName("pendingOrders")] int PendingOrders,
    [property: JsonPropertyName("todayRevenue")] decimal TodayRevenue);

public sealed record VehicleItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("registration")] string Registration,
    [property: JsonPropertyName("make")] string? Make,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("currentLatitude")] double? CurrentLatitude,
    [property: JsonPropertyName("currentLongitude")] double? CurrentLongitude,
    [property: JsonPropertyName("lastGpsAt")] DateTimeOffset? LastGpsAt);

public sealed record DriverItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("lastGpsAt")] DateTimeOffset? LastGpsAt);

public sealed record TransportOrderItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("orderNumber")] string OrderNumber,
    [property: JsonPropertyName("customerName")] string CustomerName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("priority")] string Priority);

public sealed record TripItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("executionState")] string ExecutionState,
    [property: JsonPropertyName("currentLatitude")] double? CurrentLatitude,
    [property: JsonPropertyName("currentLongitude")] double? CurrentLongitude,
    [property: JsonPropertyName("routeProgressPercent")] double RouteProgressPercent,
    [property: JsonPropertyName("lastGpsAt")] DateTimeOffset? LastGpsAt);

public sealed record GeoPoint(
    [property: JsonPropertyName("lat")] double Lat,
    [property: JsonPropertyName("lon")] double Lon);

public sealed record RouteRequest(
    [property: JsonPropertyName("start")] GeoPoint Start,
    [property: JsonPropertyName("destination")] GeoPoint Destination,
    [property: JsonPropertyName("profile")] string Profile = "truck",
    [property: JsonPropertyName("avoidRestricted")] bool AvoidRestricted = true);

public sealed record RouteResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("routes")] IReadOnlyList<RouteCandidate> Routes,
    [property: JsonPropertyName("selectedRouteIndex")] int SelectedRouteIndex,
    [property: JsonPropertyName("isTruckSafe")] bool IsTruckSafe,
    [property: JsonPropertyName("violations")] IReadOnlyList<RestrictionViolation> Violations,
    [property: JsonPropertyName("summary")] RouteSummary? Summary,
    [property: JsonPropertyName("diagnostics")] RouteDiagnostics? Diagnostics,
    [property: JsonPropertyName("maneuvers")] IReadOnlyList<RouteManeuver> Maneuvers);

public sealed record RouteCandidate(
    [property: JsonPropertyName("distance")] double Distance,
    [property: JsonPropertyName("duration")] double Duration,
    [property: JsonPropertyName("geometry")] JsonElement? Geometry,
    [property: JsonPropertyName("analysis")] RouteAnalysis? Analysis);

public sealed record RouteAnalysis(
    [property: JsonPropertyName("restricted")] bool Restricted,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("violations")] IReadOnlyList<RestrictionViolation> Violations,
    [property: JsonPropertyName("debug")] RouteDebug? Debug);

public sealed record RouteDebug(
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("startSnap")] string? StartSnap,
    [property: JsonPropertyName("endSnap")] string? EndSnap,
    [property: JsonPropertyName("traversalCount")] int TraversalCount,
    [property: JsonPropertyName("highlights")] IReadOnlyList<string> Highlights);

public sealed record RestrictionViolation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record RouteSummary(
    [property: JsonPropertyName("distanceMeters")] double DistanceMeters,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("estimatedArrival")] DateTimeOffset? EstimatedArrival);

public sealed record RouteDiagnostics(
    [property: JsonPropertyName("engine")] string Engine,
    [property: JsonPropertyName("usedFallback")] bool UsedFallback,
    [property: JsonPropertyName("expandedStates")] int ExpandedStates,
    [property: JsonPropertyName("graphVersion")] long? GraphVersion,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("startSnap")] string? StartSnap,
    [property: JsonPropertyName("endSnap")] string? EndSnap,
    [property: JsonPropertyName("traversalCount")] int TraversalCount,
    [property: JsonPropertyName("highlights")] IReadOnlyList<string> Highlights);

public sealed record RouteManeuver(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("instruction")] string Instruction,
    [property: JsonPropertyName("distanceFromPreviousMeters")] double DistanceFromPreviousMeters,
    [property: JsonPropertyName("distanceFromRouteStartMeters")] double DistanceFromRouteStartMeters,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("roadName")] string? RoadName,
    [property: JsonPropertyName("roadRef")] string? RoadRef,
    [property: JsonPropertyName("roundaboutExit")] int? RoundaboutExit);
