using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class PostgresRestrictionEngine(
    IPostgresRestrictionRepository repository)
    : IRestrictionEngine
{
    public async Task<IReadOnlyList<RestrictionViolation>> AnalyzeAsync(
        IEnumerable<GeoPoint> routePoints,
        TruckProfile? truck,
        DateTimeOffset? departureAt,
        CancellationToken ct)
    {
        if (truck is null)
        {
            return [];
        }

        var points = routePoints
            .Where(p =>
                p.Lat >= -90 &&
                p.Lat <= 90 &&
                p.Lon >= -180 &&
                p.Lon <= 180)
            .ToList();

        if (points.Count == 0)
        {
            return [];
        }

        var restrictions = await repository.FindNearRouteAsync(
            points,
            80,
            ct);

        var result = new List<RestrictionViolation>();

        foreach (var restriction in restrictions)
        {
            if (!Applies(restriction, truck, departureAt))
            {
                continue;
            }

            result.Add(new RestrictionViolation
            {
                Id = restriction.Id.ToString(),
                Name = string.IsNullOrWhiteSpace(restriction.Name)
                    ? $"OSM way {restriction.OsmWayId}"
                    : restriction.Name,
                Type = restriction.RestrictionType,
                Reason = BuildReason(restriction, truck)
            });
        }

        return result;
    }

    private static bool Applies(
        RoadRestriction restriction,
        TruckProfile truck,
        DateTimeOffset? departureAt)
    {
        if (restriction.HgvBan && truck.IsHgv)
        {
            return true;
        }

        if (restriction.HazmatBan &&
            (truck.Hazmat || !string.IsNullOrWhiteSpace(truck.AdrClass)))
        {
            return true;
        }

        if (restriction.MaxWeightTons is not null &&
            truck.GrossWeightTons > restriction.MaxWeightTons.Value)
        {
            return true;
        }

        if (restriction.MaxHeightMeters is not null &&
            truck.HeightMeters > restriction.MaxHeightMeters.Value)
        {
            return true;
        }

        if (restriction.MaxWidthMeters is not null &&
            truck.WidthMeters > restriction.MaxWidthMeters.Value)
        {
            return true;
        }

        if (restriction.MaxLengthMeters is not null &&
            truck.LengthMeters > restriction.MaxLengthMeters.Value)
        {
            return true;
        }

        if (restriction.MaxAxleLoadTons is not null &&
            truck.AxleLoadTons is not null &&
            truck.AxleLoadTons.Value > restriction.MaxAxleLoadTons.Value)
        {
            return true;
        }

        if (departureAt is not null &&
            restriction.TimeFrom is not null &&
            restriction.TimeTo is not null)
        {
            var time = departureAt.Value.TimeOfDay;

            var active =
                restriction.TimeFrom <= restriction.TimeTo
                    ? time >= restriction.TimeFrom &&
                      time <= restriction.TimeTo
                    : time >= restriction.TimeFrom ||
                      time <= restriction.TimeTo;

            if (active)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildReason(
        RoadRestriction restriction,
        TruckProfile truck)
    {
        var reasons = new List<string>();

        if (restriction.MaxWeightTons is not null &&
            truck.GrossWeightTons > restriction.MaxWeightTons.Value)
        {
            reasons.Add(
                $"masa {truck.GrossWeightTons}t > {restriction.MaxWeightTons.Value}t");
        }

        if (restriction.MaxHeightMeters is not null &&
            truck.HeightMeters > restriction.MaxHeightMeters.Value)
        {
            reasons.Add(
                $"visina {truck.HeightMeters}m > {restriction.MaxHeightMeters.Value}m");
        }

        if (restriction.MaxWidthMeters is not null &&
            truck.WidthMeters > restriction.MaxWidthMeters.Value)
        {
            reasons.Add(
                $"širina {truck.WidthMeters}m > {restriction.MaxWidthMeters.Value}m");
        }

        if (restriction.MaxLengthMeters is not null &&
            truck.LengthMeters > restriction.MaxLengthMeters.Value)
        {
            reasons.Add(
                $"dužina {truck.LengthMeters}m > {restriction.MaxLengthMeters.Value}m");
        }

        if (restriction.MaxAxleLoadTons is not null &&
            truck.AxleLoadTons is not null &&
            truck.AxleLoadTons.Value > restriction.MaxAxleLoadTons.Value)
        {
            reasons.Add(
                $"osovinsko opterećenje {truck.AxleLoadTons.Value}t > {restriction.MaxAxleLoadTons.Value}t");
        }

        if (restriction.HgvBan && truck.IsHgv)
        {
            reasons.Add("HGV zabrana");
        }

        if (restriction.HazmatBan &&
            (truck.Hazmat || !string.IsNullOrWhiteSpace(truck.AdrClass)))
        {
            reasons.Add(
                string.IsNullOrWhiteSpace(truck.AdrClass)
                    ? "ADR / opasna roba"
                    : $"ADR {truck.AdrClass}");
        }

        if (restriction.TimeFrom is not null &&
            restriction.TimeTo is not null)
        {
            reasons.Add(
                $"vremensko ograničenje {restriction.TimeFrom}-{restriction.TimeTo}");
        }

        return reasons.Count == 0
            ? "Vozilo nije kompatibilno sa ograničenjem."
            : string.Join("; ", reasons);
    }
}