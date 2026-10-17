using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

// /api/driver/portal: the signed-in driver's running (or next planned) trip and its stops.

public sealed record DriverPortal(
    [property: JsonPropertyName("driver")] DriverPortalDriver? Driver,
    [property: JsonPropertyName("trip")] DriverPortalTrip? Trip,
    [property: JsonPropertyName("stops")] IReadOnlyList<DriverPortalStop>? Stops,
    [property: JsonPropertyName("nextStopId")] Guid? NextStopId,
    [property: JsonPropertyName("dispatchPhone")] string? DispatchPhone,
    [property: JsonPropertyName("pendingRoute")] DriverPendingRoute? PendingRoute,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("financial")] DriverPortalFinancial? Financial,
    [property: JsonPropertyName("euStay")] DriverPortalEuStay? EuStay,
    [property: JsonPropertyName("driverHistory")] IReadOnlyList<DriverPortalHistoryItem>? DriverHistory,
    [property: JsonPropertyName("vehicleHistory")] IReadOnlyList<DriverPortalHistoryItem>? VehicleHistory,
    [property: JsonPropertyName("trips")] IReadOnlyList<DriverPortalTripSummary>? Trips,
    [property: JsonPropertyName("vehicleRepairs")] IReadOnlyList<DriverPortalVehicleRepair>? VehicleRepairs);

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

public sealed record DriverPortalFinancial(
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("budgetAmount")] decimal? BudgetAmount,
    [property: JsonPropertyName("spentAmount")] decimal SpentAmount,
    [property: JsonPropertyName("driverPaidAmount")] decimal DriverPaidAmount,
    [property: JsonPropertyName("allowanceAmount")] decimal AllowanceAmount,
    [property: JsonPropertyName("paidOutAmount")] decimal PaidOutAmount,
    [property: JsonPropertyName("pendingAmount")] decimal PendingAmount,
    [property: JsonPropertyName("costs")] IReadOnlyList<DriverPortalCost>? Costs);

public sealed record DriverPortalCost(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("paymentMethod")] string? PaymentMethod,
    [property: JsonPropertyName("cardLabel")] string? CardLabel,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("countryCode")] string? CountryCode,
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("notes")] string? Notes);

public sealed record DriverPortalEuStay(
    [property: JsonPropertyName("daysUsed")] int DaysUsed,
    [property: JsonPropertyName("daysRemaining")] int DaysRemaining,
    [property: JsonPropertyName("windowStart")] DateOnly WindowStart,
    [property: JsonPropertyName("windowEnd")] DateOnly WindowEnd,
    [property: JsonPropertyName("statusText")] string StatusText,
    [property: JsonPropertyName("countries")] IReadOnlyList<string>? Countries);

public sealed record DriverPortalHistoryItem(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tripNumber")] string? TripNumber,
    [property: JsonPropertyName("orderNumber")] string? OrderNumber,
    [property: JsonPropertyName("route")] string? Route,
    [property: JsonPropertyName("startedAt")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("status")] string Status);

public sealed record DriverPortalTripSummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tripNumber")] string? TripNumber,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("route")] string? Route,
    [property: JsonPropertyName("plannedStartAt")] DateTimeOffset? PlannedStartAt,
    [property: JsonPropertyName("plannedArrivalAt")] DateTimeOffset? PlannedArrivalAt,
    [property: JsonPropertyName("startedAt")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("routeProgressPercent")] double RouteProgressPercent,
    [property: JsonPropertyName("budget")] decimal? Budget,
    [property: JsonPropertyName("spent")] decimal Spent,
    [property: JsonPropertyName("costs")] IReadOnlyList<DriverPortalCost>? Costs);

public sealed record DriverPortalVehicleRepair(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("odometerKm")] int? OdometerKm,
    [property: JsonPropertyName("cost")] decimal? Cost,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("workshop")] string? Workshop,
    [property: JsonPropertyName("resolvedAt")] DateTimeOffset? ResolvedAt);

public sealed record DriverIncidentRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("severity")] string Severity);

public sealed record DriverExpenseDraft(
    string Category,
    Guid? TripId,
    decimal Amount,
    DateTimeOffset IncurredAt,
    string? Location,
    string? CountryCode,
    string PaymentMethod,
    string? CardLabel,
    decimal? Quantity,
    decimal? UnitPrice,
    int? OdometerKm,
    Guid? VehicleId,
    string? Notes,
    byte[]? ReceiptBytes,
    string? ReceiptFileName,
    string? ReceiptContentType);

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
