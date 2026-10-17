using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

// /api/driver/portal: the signed-in driver's running (or next planned) trip and its stops.

public sealed record DriverPortal(
    [property: JsonPropertyName("driver")] DriverPortalDriver? Driver,
    [property: JsonPropertyName("trip")] DriverPortalTrip? Trip,
    [property: JsonPropertyName("stops")] IReadOnlyList<DriverPortalStop>? Stops,
    [property: JsonPropertyName("nextStopId")] Guid? NextStopId,
    [property: JsonPropertyName("dispatchPhone")] string? DispatchPhone,
    [property: JsonPropertyName("pendingRoute")] DriverPendingRoute? PendingRoute);

public sealed record DriverPortalDriver(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("employeeNumber")] string? EmployeeNumber,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("currentVehicleId")] Guid? CurrentVehicleId);

public sealed record DriverPortalTrip(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tripNumber")] string TripNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("routeProgressPercent")] double RouteProgressPercent,
    [property: JsonPropertyName("plannedArrivalAt")] DateTimeOffset? PlannedArrivalAt,
    [property: JsonPropertyName("route")] string? Route,
    [property: JsonPropertyName("vehicle")] string? Vehicle,
    [property: JsonPropertyName("vehicleId")] Guid? VehicleId);

public sealed record DriverPortalStop(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("countryCode")] string? CountryCode,
    [property: JsonPropertyName("plannedAt")] DateTimeOffset? PlannedAt,
    [property: JsonPropertyName("arrivedAt")] DateTimeOffset? ArrivedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude);

public sealed record DriverPendingRoute(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tripId")] Guid TripId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("sentAt")] DateTimeOffset? SentAt);

public sealed record DriverIncidentRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("severity")] string Severity);

public sealed record DriverRejectRouteRequest(
    [property: JsonPropertyName("reason")] string? Reason);

/// <summary>Body of POST /api/navigation/gps (NavigationTelemetryController).</summary>
public sealed record GpsPositionRequest(
    [property: JsonPropertyName("vehicleId")] Guid VehicleId,
    [property: JsonPropertyName("tripId")] Guid? TripId,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("accuracyMeters")] double? AccuracyMeters,
    [property: JsonPropertyName("speedKmh")] double? SpeedKmh,
    [property: JsonPropertyName("bearing")] double? Bearing,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);
