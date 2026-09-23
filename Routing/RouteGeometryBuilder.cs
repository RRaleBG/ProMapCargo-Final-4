using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace ProMapCargo.Api.Routing;

public sealed record RouteGeometryBuildResult(
    bool Success,
    IReadOnlyList<double[]> Coordinates,
    string? FailureReason,
    int OriginalPointCount,
    int OrientedPointCount,
    int TrimmedPointCount,
    int FinalPointCount);

public static class RouteGeometryBuilder
{
    private const double CoordinateTolerance = 1e-8;

    public static RouteGeometryBuildResult Build(
        IReadOnlyList<RoutedTraversal> traversals,
        IReadOnlyDictionary<long, RoadEdge> edges,
        SnapResult? startSnap,
        SnapResult? endSnap)
    {
        if (traversals.Count == 0)
        {
            return Failure("No traversals were provided.");
        }

        var coordinates = new List<double[]>();
        var originalPointCount = 0;
        var orientedPointCount = 0;
        var trimmedPointCount = 0;

        for (var traversalIndex = 0; traversalIndex < traversals.Count; traversalIndex++)
        {
            var traversal = traversals[traversalIndex];

            if (!edges.TryGetValue(traversal.EdgeId, out var edge))
            {
                return Failure($"Route edge {traversal.EdgeId} was not loaded.");
            }

            if (edge.Geometry is not LineString line || line.Coordinates.Length < 2)
            {
                return Failure($"Route edge {edge.Id} is missing a valid LineString geometry.");
            }

            originalPointCount += line.Coordinates.Length;

            var orientedLine = traversal.Forward
                ? line
                : (LineString)line.Reverse();

            orientedPointCount += orientedLine.Coordinates.Length;

            var startFraction = traversalIndex == 0
                ? OrientedFraction(startSnap?.Fraction, traversal.Forward)
                : 0d;

            var endFraction = traversalIndex == traversals.Count - 1
                ? OrientedFraction(endSnap?.Fraction, traversal.Forward)
                : 1d;

            var segment = ExtractSegment(orientedLine, startFraction, endFraction);
            trimmedPointCount += segment.Count;

            AppendSegment(coordinates, segment);
        }

        if (coordinates.Count == 1)
        {
            coordinates.Add(coordinates[0].ToArray());
        }

        if (coordinates.Count < 2)
        {
            return Failure("Route geometry collapsed to fewer than two coordinates.");
        }

        return new RouteGeometryBuildResult(
            true,
            coordinates,
            null,
            originalPointCount,
            orientedPointCount,
            trimmedPointCount,
            coordinates.Count);
    }

    private static RouteGeometryBuildResult Failure(string reason)
        => new(false, [], reason, 0, 0, 0, 0);

    private static double OrientedFraction(double? fraction, bool forward)
    {
        var value = Clamp01(fraction ?? 0d);
        return forward ? value : 1d - value;
    }

    private static IReadOnlyList<double[]> ExtractSegment(LineString line, double startFraction, double endFraction)
    {
        var start = Clamp01(startFraction);
        var end = Clamp01(endFraction);

        if (end < start)
        {
            (start, end) = (end, start);
        }

        var lengthIndexedLine = new LengthIndexedLine(line);
        var startIndex = line.Length * start;
        var endIndex = line.Length * end;
        var extracted = lengthIndexedLine.ExtractLine(startIndex, endIndex);

        return GeometryToCoordinates(extracted);
    }

    private static IReadOnlyList<double[]> GeometryToCoordinates(Geometry geometry)
    {
        if (geometry is LineString line)
        {
            return line.Coordinates
                .Select(static coordinate => new[] { coordinate.X, coordinate.Y })
                .ToList();
        }

        if (geometry is Point point)
        {
            return new List<double[]>
            {
                new[] { point.X, point.Y },
                new[] { point.X, point.Y }
            };
        }

        if (geometry is GeometryCollection collection)
        {
            var coordinates = new List<double[]>();

            foreach (var child in collection.Geometries)
            {
                coordinates.AddRange(GeometryToCoordinates(child));
            }

            return coordinates;
        }

        return geometry.Coordinates
            .Select(static coordinate => new[] { coordinate.X, coordinate.Y })
            .ToList();
    }

    private static void AppendSegment(List<double[]> target, IReadOnlyList<double[]> segment)
    {
        foreach (var point in segment)
        {
            if (target.Count > 0 && AreClose(target[^1], point))
            {
                continue;
            }

            target.Add(point);
        }
    }

    private static bool AreClose(double[] a, double[] b)
    {
        if (a.Length < 2 || b.Length < 2)
        {
            return false;
        }

        return Math.Abs(a[0] - b[0]) <= CoordinateTolerance &&
               Math.Abs(a[1] - b[1]) <= CoordinateTolerance;
    }

    private static double Clamp01(double value)
        => value < 0d ? 0d : value > 1d ? 1d : value;
}
