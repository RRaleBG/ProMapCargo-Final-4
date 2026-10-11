namespace ProMapCargo.OfflineBundleBuilder;

/// <summary>
/// Default truck profile and the per-road rules, mirroring Routing/TruckEdgeEvaluator.cs
/// on the server (40 t, 4.0 m high, 2.55 m wide, 16.5 m long, HGV, commercial, max 90
/// km/h), so offline routes match online ones. Keep both in step when rules change.
/// </summary>
public static class TruckRules
{
    public const double GrossWeightTons = 40;
    public const double HeightMeters = 4.0;
    public const double WidthMeters = 2.55;
    public const double LengthMeters = 16.5;
    public const double MaxSpeedKmh = 90;

    public sealed record RoadRow(
        long Id,
        long SourceNode,
        long TargetNode,
        short Direction,
        string? Highway,
        string? Name,
        string? Ref,
        string? Access,
        string? Vehicle,
        string? MotorVehicle,
        string? Hgv,
        string? Goods,
        string? Hazmat,
        double? MaxHeight,
        double? MaxWidth,
        double? MaxLength,
        double? MaxWeight,
        double SpeedKmh,
        double LengthM,
        double[] Coordinates);

    public static bool Allowed(RoadRow road)
    {
        if (!IsAllowed(road.Access) || !IsAllowed(road.Vehicle) || !IsAllowed(road.MotorVehicle))
        {
            return false;
        }

        if (IsNo(road.Hgv) || IsNo(road.Goods))
        {
            return false;
        }

        return !(road.MaxHeight is { } h && HeightMeters > h) &&
               !(road.MaxWidth is { } w && WidthMeters > w) &&
               !(road.MaxLength is { } l && LengthMeters > l) &&
               !(road.MaxWeight is { } t && GrossWeightTons > t);
    }

    public static double Speed(RoadRow road)
    {
        var baseSpeed = road.SpeedKmh > 0 ? road.SpeedKmh : DefaultTruckSpeed(road.Highway);
        var adjusted = Math.Min(baseSpeed, MaxSpeedKmh);
        adjusted *= AccessFactor(road);
        adjusted *= HighwayFactor(road.Highway);
        return Math.Max(5, adjusted);
    }

    /// <summary>Road class code for the bundle (used for display / future weighting).</summary>
    public static byte ClassCode(string? highway) => highway?.ToLowerInvariant() switch
    {
        "motorway" => 1,
        "motorway_link" => 2,
        "trunk" => 3,
        "trunk_link" => 4,
        "primary" => 5,
        "primary_link" => 6,
        "secondary" => 7,
        "secondary_link" => 8,
        "tertiary" => 9,
        "unclassified" => 10,
        "residential" => 11,
        "living_street" => 12,
        "service" => 13,
        _ => 0,
    };

    private static bool IsAllowed(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (!value.Equals("no", StringComparison.OrdinalIgnoreCase) &&
         !value.Equals("private", StringComparison.OrdinalIgnoreCase) &&
         !value.Equals("restricted", StringComparison.OrdinalIgnoreCase));

    private static bool IsNo(string? value) => value?.Equals("no", StringComparison.OrdinalIgnoreCase) == true;

    private static double DefaultTruckSpeed(string? highway) => highway?.ToLowerInvariant() switch
    {
        "motorway" => 90,
        "motorway_link" => 55,
        "trunk" => 85,
        "trunk_link" => 50,
        "primary" => 72,
        "primary_link" => 45,
        "secondary" => 62,
        "secondary_link" => 40,
        "tertiary" => 50,
        "unclassified" => 40,
        "residential" => 28,
        "living_street" => 10,
        "service" => 14,
        _ => 30,
    };

    private static double HighwayFactor(string? highway) => highway?.ToLowerInvariant() switch
    {
        "motorway" or "trunk" => 1.0,
        "motorway_link" or "trunk_link" => 0.92,
        "primary" or "primary_link" => 0.92,
        "secondary" or "secondary_link" => 0.84,
        "tertiary" => 0.74,
        "unclassified" => 0.66,
        "residential" => 0.56,
        "living_street" => 0.4,
        "service" => 0.42,
        _ => 0.7,
    };

    private static double AccessFactor(RoadRow road) =>
        new[] { road.Access, road.Vehicle, road.MotorVehicle, road.Hgv, road.Goods }
            .Select(SoftRestrictionFactor)
            .Min();

    private static double SoftRestrictionFactor(string? value) => value?.ToLowerInvariant() switch
    {
        "destination" => 0.45,
        "delivery" => 0.5,
        "customers" => 0.5,
        "discouraged" => 0.55,
        "permissive" => 0.7,
        "yes" or "designated" or null => 1.0,
        _ => 0.8,
    };
}
