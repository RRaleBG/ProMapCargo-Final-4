using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Routing;
public sealed class TruckEdgeEvaluator
{
    public bool Allowed(RoadEdge edge, TruckProfile truck, out string? reason)
    {
        reason = null;
        if (!IsAllowed(edge.Access, "access"))
        {
            reason = $"access={edge.Access}";
            return false;
        }
        if (!IsAllowed(edge.Vehicle, "vehicle") || !IsAllowed(edge.MotorVehicle, "motor_vehicle"))
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
        if (edge.MaxAxleLoad is not null
        && truck.AxleLoadTons is not null
        && (double)truck.AxleLoadTons > edge.MaxAxleLoad)
        {
            reason = "axleload";
            return false;
        }
        return true;
    }
    public double Speed(RoadEdge edge, TruckProfile truck) =>
    Math.Max(5, Math.Min(edge.SpeedKmh > 0 ? edge.SpeedKmh : 30, (double)truck.MaxSpeedKmh));
    private static bool IsAllowed(string? value, string _)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        return !value.Equals("no", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("private", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("restricted", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsNo(string? value) =>
    value?.Equals("no", StringComparison.OrdinalIgnoreCase) == true;
}
