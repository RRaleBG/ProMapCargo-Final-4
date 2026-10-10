using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Routing;

public sealed class ManeuverBuilder
{
    public IReadOnlyList<RouteManeuverDto> Build(IReadOnlyList<RoutedTraversal> traversals, IReadOnlyList<RoadEdge> edges)
    {
        var map = edges.ToDictionary(x => x.Id);
        var result = new List<RouteManeuverDto>();
        double cumulative = 0;

        for (var i = 0; i < traversals.Count; i++)
        {
            var traversal = traversals[i];

            if (!map.TryGetValue(traversal.EdgeId, out var edge))
            {
                continue;
            }

            // The maneuver happens where the traversal starts, which is the last
            // stored vertex when the edge is travelled against its stored direction.
            var edgeCoordinates = edge.Geometry.Coordinates;
            var coordinate = edgeCoordinates.Length == 0
                ? null
                : traversal.Forward ? edgeCoordinates[0] : edgeCoordinates[^1];
            var type = i == 0 ? "Depart" : i == traversals.Count - 1 ? "Arrive" : "Continue";

            if (i > 0 &&
                map.TryGetValue(traversals[i - 1].EdgeId, out var previousEdge) &&
                previousEdge.Geometry.Coordinates.Length >= 2 &&
                edgeCoordinates.Length >= 2)
            {
                // Edges are polylines between junctions: compare the heading of the
                // LAST segment of the previous traversal with the FIRST segment of
                // this one, both in travel direction.
                var previousHeading = ExitHeading(previousEdge.Geometry.Coordinates, traversals[i - 1].Forward);
                var nextHeading = EntryHeading(edgeCoordinates, traversal.Forward);
                var delta = ((nextHeading - previousHeading + 540) % 360) - 180;

                if (delta > 25)
                {
                    type = "Right";
                }
                else if (delta < -25)
                {
                    type = "Left";
                }
            }

            var roadName = string.IsNullOrWhiteSpace(edge.Name)
                ? edge.Highway
                : edge.Name;

            var instruction = type switch
            {
                "Depart" => string.IsNullOrWhiteSpace(roadName)
                    ? "Krenite"
                    : $"Krenite na {roadName}",
                "Arrive" => "Stigli ste",
                "Left" => string.IsNullOrWhiteSpace(roadName)
                    ? "Skrenite levo"
                    : $"Skrenite levo na {roadName}",
                "Right" => string.IsNullOrWhiteSpace(roadName)
                    ? "Skrenite desno"
                    : $"Skrenite desno na {roadName}",
                _ => string.IsNullOrWhiteSpace(roadName)
                    ? "Nastavite pravo"
                    : $"Nastavite na {roadName}",
            };

            result.Add(new RouteManeuverDto(
                i,
                type,
                instruction,
                traversal.DistanceM,
                cumulative,
                coordinate?.Y ?? 0,
                coordinate?.X ?? 0,
                edge.Name,
                edge.Ref,
                null));

            cumulative += traversal.DistanceM;
        }

        return result;
    }

    static double ExitHeading(NetTopologySuite.Geometries.Coordinate[] c, bool forward) =>
        forward ? Heading(c[^2], c[^1]) : Heading(c[1], c[0]);

    static double EntryHeading(NetTopologySuite.Geometries.Coordinate[] c, bool forward) =>
        forward ? Heading(c[0], c[1]) : Heading(c[^1], c[^2]);

    /// <summary>Initial bearing in degrees from a to b.</summary>
    static double Heading(NetTopologySuite.Geometries.Coordinate a, NetTopologySuite.Geometries.Coordinate b)
    {
        var d = (b.X - a.X) * Math.PI / 180;
        var p1 = a.Y * Math.PI / 180;
        var p2 = b.Y * Math.PI / 180;
        return (Math.Atan2(Math.Sin(d) * Math.Cos(p2), Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(d)) * 180 / Math.PI + 360) % 360;
    }
}
