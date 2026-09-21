using Microsoft.AspNetCore.Identity;
namespace ProMapCargo.Api.Models;
public sealed class Company {
    public Guid Id { get; set; } public string Name { get; set; } = "";
    public string? LegalName { get; set; } public string? TaxNumber { get; set; } public string CountryCode { get; set; } = "RS";
    public string? Address { get; set; } public string? City { get; set; } public string? PostalCode { get; set; } public bool
        IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ApplicationUser : IdentityUser<Guid> {
    public Guid? CompanyId { get; set; } public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Company? Company { get; set; }
}
public sealed class ApplicationRole : IdentityRole<Guid> {
}
public sealed class RolePermission {
    public Guid RoleId { get; set; } public string Permission { get; set; } = "";
    public ApplicationRole? Role { get; set; }
}
public enum DriverStatus {
    Active, Inactive, OnLeave, Suspended
}
public sealed class Driver {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid? UserId { get; set; } public string FullName { get; set; } = "";
    public string? Phone { get; set; } public string? LicenseNumber { get; set; } public DriverStatus Status { get; set; } =
        DriverStatus.Active;
    public Guid? CurrentVehicleId { get; set; } public Guid? CurrentTripId { get; set; } public string? CurrentCountryCode { get; set;
        } public DateTimeOffset? LastGpsAt { get; set; }
}
public enum VehicleType {
    Truck, Tractor, Trailer, Van, Bus, Car
}
public enum FuelType {
    Diesel, Petrol, LNG, CNG, Electric, Hybrid, Other
}
public enum VehicleStatus {
    Active, Inactive, Maintenance, OutOfService
}
public sealed class Vehicle {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public string Registration { get; set; } = "";
    public string? VIN { get; set; } public string? Make { get; set; } public string? Model { get; set; } public int? Year { get; set;
        } public VehicleType Type { get; set; } = VehicleType.Tractor;
    public FuelType Fuel { get; set; } = FuelType.Diesel;
    public VehicleStatus Status { get; set; } = VehicleStatus.Active;
    public Guid? CurrentDriverId { get; set; } public Guid? CurrentTripId { get; set; } public decimal GrossWeightTons { get; set; } = 40;
    public decimal AxleLoadTons { get; set; } = 10;
    public decimal LengthMeters { get; set; } = 16.5m;
    public decimal WidthMeters { get; set; } = 2.55m;
    public decimal HeightMeters { get; set; } = 4m;
    public int Axles { get; set; } = 5;
    public decimal MaxSpeedKmh { get; set; } = 90;
    public bool Commercial { get; set; } = true;
    public bool Hazmat { get; set; }
}
public enum TransportOrderStatus {
    Draft, Planned, Assigned, Dispatched, Loading, Loaded, InTransit, AtBorder, Unloading, Delivered, Closed, Cancelled
}
public enum TransportOrderPriority {
    Low, Normal, High, Critical
}
public sealed class TransportOrder {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public string OrderNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public TransportOrderStatus Status { get; set; } = TransportOrderStatus.Draft;
    public TransportOrderPriority Priority { get; set; } = TransportOrderPriority.Normal;
    public string? CargoDescription { get; set; } public decimal? CargoWeightTons { get; set; } public decimal? CargoVolumeM3 { get;
        set; } public int? Pallets { get; set; } public bool Hazmat { get; set; } public Guid? DriverId { get; set; } public Guid?
        VehicleId { get; set; } public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public enum TransportStopType {
    Loading, Unloading, LoadingAndUnloading, Border, Rest, Fuel, Other
}
public enum TransportStopStatus {
    Planned, Arrived, InService, Completed, Skipped
}
public sealed class TransportStop {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid TransportOrderId { get; set; } public int Sequence {
        get; set; } public TransportStopType Type { get; set; } public TransportStopStatus Status { get; set; } =
        TransportStopStatus.Planned;
    public string Name { get; set; } = "";
    public string? Address { get; set; } public string? City { get; set; } public string? CountryCode { get; set; } public double
        Latitude { get; set; } public double Longitude { get; set; } public DateTimeOffset? PlannedAt { get; set; } public DateTimeOffset?
        ArrivedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } public int WaitingMinutes { get; set; } public int
        ServiceMinutes { get; set; } public string? Notes { get; set; }
}
public enum TripStatus {
    Planned, Active, Paused, Completed, Cancelled
}
public enum TripExecutionState {
    Planned, Assigned, ReadyForDeparture, Loading, Loaded, InTransit, ApproachingStop, AtStop, Waiting, BorderCrossing, Unloading,
        Completed, Cancelled, Suspended
}
public sealed class Trip {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid TransportOrderId { get; set; } public Guid? DriverId
        { get; set; } public Guid? VehicleId { get; set; } public TripStatus Status { get; set; } = TripStatus.Planned;
    public TripExecutionState ExecutionState { get; set; } = TripExecutionState.Planned;
    public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } public double
        PlannedDistanceMeters { get; set; } public double PlannedDurationSeconds { get; set; } public double RouteCoveredMeters { get;
        set; } public double RouteRemainingMeters { get; set; } public double RouteProgressPercent { get; set; } public double
        DistanceToRouteMeters { get; set; } public bool IsOffRoute { get; set; } public double? CurrentLatitude { get; set; } public
        double? CurrentLongitude { get; set; } public double? CurrentSpeedKmh { get; set; } public double? CurrentBearing { get; set; }
        public DateTimeOffset? LastGpsAt { get; set; } public Guid? ActiveRouteId { get; set; }
}
public enum TripRouteStatus {
    Active, Superseded, Cancelled
}
public sealed class TripRoute {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid TripId { get; set; } public long GraphVersion { get;
        set; } public TripRouteStatus Status { get; set; } = TripRouteStatus.Active;
    public double DistanceMeters { get; set; } public double DurationSeconds { get; set; } public DateTimeOffset? EstimatedArrival { get; set; } public string Engine { get; set; } = "PostGIS";
    public bool UsedFallback { get; set; } public int ExpandedStates { get; set; } public string GeometryJson { get; set; } = "{}";
    public string ManeuversJson { get; set; } = "[]";
    public string EdgeIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ActivatedAt { get; set; }
}
public enum RouteDispatchStatus {
    Pending, Sent, Accepted, Rejected, Started, Superseded, Cancelled, Expired
}
public sealed class RouteDispatch {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid TripId { get; set; } public Guid TripRouteId { get;
        set; } public Guid DriverId { get; set; } public Guid VehicleId { get; set; } public RouteDispatchStatus Status { get; set; } =
        RouteDispatchStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; } public DateTimeOffset? AcceptedAt { get; set; } public DateTimeOffset? RejectedAt {
        get; set; } public DateTimeOffset? StartedAt { get; set; } public string? RejectionReason { get; set; } public Guid? SentByUserId
        { get; set; }
}
public enum PositionSource {
    Gps, Network, Ip, LastKnown, Manual
}
public enum PositionQuality {
    Excellent, Good, Degraded, Lost, Approximate
}
public sealed class VehiclePositionEvent {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid VehicleId { get; set; } public Guid? TripId { get;
        set; } public double Latitude { get; set; } public double Longitude { get; set; } public double? AccuracyMeters { get; set; }
        public double? SpeedKmh { get; set; } public double? Bearing { get; set; } public DateTimeOffset Timestamp { get; set; } public
        PositionSource Source { get; set; } public PositionQuality Quality { get; set; }
}
public sealed class DeliveryConfirmation {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid TransportOrderId { get; set; } public Guid TripId {
        get; set; } public Guid StopId { get; set; } public Guid? DriverId { get; set; } public DateTimeOffset ConfirmedAt { get; set; } =
        DateTimeOffset.UtcNow;
    public string? RecipientName { get; set; } public string? SignatureReference { get; set; } public string? DocumentReference { get;
        set; } public string? Notes { get; set; }
}
public sealed class AuditLog {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid? UserId { get; set; } public DateTimeOffset CreatedAt
        { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? OldValueJson { get; set; } public string? NewValueJson { get; set; } public string? IpAddress { get; set; } public string Source { get; set; } = "Web";
}
public sealed class OperationalAlert {
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public string Type { get; set; } = "System";
    public string Severity { get; set; } = "Info";
    public string Status { get; set; } = "Open";
    public Guid? TripId { get; set; } public Guid? VehicleId { get; set; } public Guid? DriverId { get; set; } public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; } public string Source { get; set; } = "System";
    public string? MetadataJson { get; set; }
}
public enum WaitingReason {
    LoadingDelay, UnloadingDelay, BorderQueue, BorderInspection, Traffic, CustomerDelay, Documentation, VehicleIssue, Other
}
public sealed class WaitingEvent {
    public Guid Id{get;set;} public Guid CompanyId{get;set;} public Guid TransportOrderId{get;set;} public Guid TripId{get;set;} public Guid? DriverId{get;set;} public Guid? VehicleId{get;set;} public Guid? StopId{get;set;} public WaitingReason Reason{get;set;} public DateTimeOffset StartedAt{get;set;} public DateTimeOffset? EndedAt{get;set;} public int DurationMinutes{get;set;} public decimal CostAmount{get;set;} public string Currency{get;set;}="EUR";
    public string? Notes{get;set;}
}
public enum BorderCrossingStatus {
    Approaching, Queued, Inspection, Cleared, Rejected, Cancelled
}
public sealed class BorderCrossing {
    public Guid Id{get;set;} public Guid CompanyId{get;set;} public Guid? TransportOrderId{get;set;} public Guid? TripId{get;set;} public Guid? DriverId{get;set;} public Guid? VehicleId{get;set;} public string CountryFrom{get;set;}="";
    public string CountryTo{get;set;}="";
    public string BorderPoint{get;set;}="";
    public double? Latitude{get;set;} public double? Longitude{get;set;} public DateTimeOffset? EnteredAt{get;set;} public
        DateTimeOffset? ClearedAt{get;set;} public int WaitingMinutes{get;set;} public bool Inspection{get;set;} public string?
        DocumentsJson{get;set;} public BorderCrossingStatus Status{get;set;}=BorderCrossingStatus.Approaching;
    public decimal CostAmount{get;set;} public string Currency{get;set;}="EUR";
    public string? Notes{get;set;}
}
public sealed class RouteCountrySegment {
    public Guid Id{get;set;} public Guid CompanyId{get;set;} public Guid TripRouteId{get;set;} public int Sequence{get;set;} public string CountryCode{get;set;}="";
    public double DistanceMeters{get;set;} public double DurationSeconds{get;set;} public DateTimeOffset? EstimatedEntry{get;set;}
        public DateTimeOffset? EstimatedExit{get;set;} public Guid? BorderCrossingId{get;set;}
}
public enum TransportCostCategory {
    Fuel,Toll,Border,Parking,Waiting,Ferry,Driver,Maintenance,Other
}
public enum TransportCostSource {
    Manual,Automatic,Imported,Estimated
}
public sealed class TransportCost {
    public Guid Id{get;set;} public Guid CompanyId{get;set;} public Guid? TransportOrderId{get;set;} public Guid? TripId{get;set;}
        public TransportCostCategory Category{get;set;} public TransportCostSource Source{get;set;}=TransportCostSource.Manual;
    public string? ReferenceType{get;set;} public Guid? ReferenceId{get;set;} public decimal Quantity{get;set;} public string Unit{get;set;}="unit";
    public decimal UnitPrice{get;set;} public decimal Amount{get;set;} public string Currency{get;set;}="EUR";
    public bool IsEstimated{get;set;} public DateTimeOffset CreatedAt{get;set;}=DateTimeOffset.UtcNow;
    public string? Notes{get;set;}
}
