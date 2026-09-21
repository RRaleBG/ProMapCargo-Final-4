using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Routing;

public sealed class TruckEdgeEvaluator
{
    public bool Allowed(RoadEdge edge, TruckProfile truck, out string? reason)
    {
        reason = null;

        if (!IsAllowed(edge.Access))
        {
            reason = $"access={edge.Access}";
            return false;
        }

        if (!IsAllowed(edge.Vehicle) || !IsAllowed(edge.MotorVehicle))
        {
            reason = "vehicle restriction";
            return false;
        }

        if (IsNo(edge.Hgv) && truck.IsHgv)
        {
            reason = "hgv restriction";
            return false;
        }

        if (IsNo(edge.Goods) && truck.Commercial)
        {
            reason = "goods restriction";
            return false;
        }

        if (IsNo(edge.Hazmat) && truck.Hazmat)
        {
            reason = "hazmat restriction";
            return false;
        }

        if (edge.MaxHeight is not null && (double)truck.HeightMeters > edge.MaxHeight)
        {
            reason = "height";
            return false;
        }

        if (edge.MaxWidth is not null && (double)truck.WidthMeters > edge.MaxWidth)
        {
            reason = "width";
            return false;
        }

        if (edge.MaxLength is not null && (double)truck.LengthMeters > edge.MaxLength)
        {
            reason = "length";
            return false;
        }

        if (edge.MaxWeight is not null && (double)truck.GrossWeightTons > edge.MaxWeight)
        {
            reason = "weight";
            return false;
        }

        if (edge.MaxAxleLoad is not null &&
            truck.AxleLoadTons is not null &&
            (double)truck.AxleLoadTons > edge.MaxAxleLoad)
        {
            reason = "axleload";
            return false;
        }

        return true;
    }

    public double Speed(RoadEdge edge, TruckProfile truck)
    {
        var baseSpeed = edge.SpeedKmh > 0
            ? edge.SpeedKmh
            : DefaultTruckSpeed(edge.Highway);

        var adjustedSpeed = Math.Min(baseSpeed, (double)truck.MaxSpeedKmh);
        adjustedSpeed *= AccessFactor(edge.Access, edge.Vehicle, edge.MotorVehicle, edge.Hgv, edge.Goods);
        adjustedSpeed *= HighwayFactor(edge.Highway);

        return Math.Max(5, adjustedSpeed);
    }

    private static bool IsAllowed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return !value.Equals("no", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("private", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("restricted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNo(string? value) =>
        value?.Equals("no", StringComparison.OrdinalIgnoreCase) == true;

    private static double DefaultTruckSpeed(string? highway) =>
        highway?.ToLowerInvariant() switch
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

    private static double HighwayFactor(string? highway) =>
        highway?.ToLowerInvariant() switch
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

    private static double AccessFactor(
        string? access,
        string? vehicle,
        string? motorVehicle,
        string? hgv,
        string? goods)
    {
        var factor = 1d;

        factor = Math.Min(factor, SoftRestrictionFactor(access));
        factor = Math.Min(factor, SoftRestrictionFactor(vehicle));
        factor = Math.Min(factor, SoftRestrictionFactor(motorVehicle));
        factor = Math.Min(factor, SoftRestrictionFactor(hgv));
        factor = Math.Min(factor, SoftRestrictionFactor(goods));

        return factor;
    }

    private static double SoftRestrictionFactor(string? value) =>
        value?.ToLowerInvariant() switch
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
