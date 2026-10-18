using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

// /api/driver/messages: what concerns the signed-in driver (alerts, routes, own incidents).
public sealed record DriverMessage(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("tripId")] Guid? TripId,
    [property: JsonPropertyName("tripNumber")] string? TripNumber);

// /api/driver/profile: the driver's record (documents with expiry, vehicle, trips done).
public sealed record DriverProfile(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("employeeNumber")] string? EmployeeNumber,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("licenseNumber")] string? LicenseNumber,
    [property: JsonPropertyName("licenseCategories")] string? LicenseCategories,
    [property: JsonPropertyName("licenseExpiry")] DateOnly? LicenseExpiry,
    [property: JsonPropertyName("code95Expiry")] DateOnly? Code95Expiry,
    [property: JsonPropertyName("adrExpiry")] DateOnly? AdrExpiry,
    [property: JsonPropertyName("tachographCardNumber")] string? TachographCardNumber,
    [property: JsonPropertyName("tachographCardExpiry")] DateOnly? TachographCardExpiry,
    [property: JsonPropertyName("medicalExpiry")] DateOnly? MedicalExpiry,
    [property: JsonPropertyName("hireDate")] DateOnly? HireDate,
    [property: JsonPropertyName("vehicle")] DriverProfileVehicle? Vehicle,
    [property: JsonPropertyName("completedTrips")] int CompletedTrips,
    [property: JsonPropertyName("completedTripsThisMonth")] int CompletedTripsThisMonth);

public sealed record DriverProfileVehicle(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("registration")] string Registration,
    [property: JsonPropertyName("make")] string? Make,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("grossWeightTons")] decimal GrossWeightTons,
    [property: JsonPropertyName("heightMeters")] decimal HeightMeters,
    [property: JsonPropertyName("widthMeters")] decimal WidthMeters,
    [property: JsonPropertyName("lengthMeters")] decimal LengthMeters,
    [property: JsonPropertyName("axles")] int Axles);
