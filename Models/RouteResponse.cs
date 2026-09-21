namespace ProMapCargo.Api.Models;

public sealed class RouteResponse
{
    public string Code { get; init; } = "Ok";

    public List<RouteCandidate> Routes { get; init; } = [];

    public int SelectedRouteIndex { get; init; }

    public bool IsTruckSafe { get; init; }

    public List<RestrictionViolation> Violations { get; init; } = [];

    public RouteSummary? Summary { get; init; }

    public RouteDiagnostics Diagnostics { get; init; } = new();

    public List<RouteManeuverDto> Maneuvers { get; init; } = [];
}

public sealed class RouteCandidate
{
    public double Distance { get; init; }

    public double Duration { get; init; }

    public object? Geometry { get; init; }

    public object? Legs { get; init; }

    public RouteAnalysis Analysis { get; init; } = new();
}

public sealed class RouteAnalysis
{
    public bool Restricted { get; init; }

    public int Score { get; init; }

    public List<RestrictionViolation> Violations { get; init; } = [];
}

public sealed class RestrictionViolation
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Type { get; init; } = "";

    public string Reason { get; init; } = "";
}

public sealed record RouteSummary(
    double DistanceMeters,
    double DurationSeconds,
    DateTimeOffset? EstimatedArrival);

public sealed class RouteDiagnostics
{
    public string Engine { get; set; } = "";

    public bool UsedFallback { get; set; }

    public int ExpandedStates { get; set; }

    public long? GraphVersion { get; set; }

    public string? FailureReason { get; set; }
}

public sealed record RouteManeuverDto(
    int Index,
    string Type,
    string Instruction,
    double DistanceFromPreviousMeters,
    double DistanceFromRouteStartMeters,
    double Latitude,
    double Longitude,
    string? RoadName,
    string? RoadRef,
    int? RoundaboutExit);