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

            var coordinate = edge.Geometry.Coordinates.FirstOrDefault();
            var type = i == 0 ? "Depart" : i == traversals.Count - 1 ? "Arrive" : "Continue";

            if (i > 0 && map.TryGetValue(traversals[i - 1].EdgeId, out var previousEdge))
            {
                var previousHeading = Heading(previousEdge.Geometry.Coordinates[^2], previousEdge.Geometry.Coordinates[^1], traversals[i - 1].Forward);
                var nextHeading = Heading(edge.Geometry.Coordinates[^2], edge.Geometry.Coordinates[^1], traversal.Forward);
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

    static double Heading(NetTopologySuite.Geometries.Coordinate a, NetTopologySuite.Geometries.Coordinate b, bool forward)
    {
        if (!forward)
        {
            (a, b) = (b, a);
        }

        var d = (b.X - a.X) * Math.PI / 180;
        var p1 = a.Y * Math.PI / 180;
        var p2 = b.Y * Math.PI / 180;
        return (Math.Atan2(Math.Sin(d) * Math.Cos(p2), Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(d)) * 180 / Math.PI + 360) % 360;
    }
}
