using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Routing;

public sealed class EdgeSnapper(
    PostGisRoutingRepository repo,
    TruckEdgeEvaluator evaluator,
    RoutingGraphCache graphCache,
    ILogger<EdgeSnapper> logger)
{
    // Junctions a short search over legal edges must reach for an edge to count as part
    // of the road network (a closed zone has a few dozen).
    private const int ConnectedJunctions = 200;

    public async Task<SnapResult?> SnapAsync(GeoPoint p, TruckProfile t, long version, CancellationToken ct)
    {
        var graph = graphCache.CreateView(version);
        var rejected = new HashSet<long>();
        foreach (var radius in new[] { 50d, 100d, 250d, 500d, 1000d })
        {
            foreach (var e in await repo.FindNearestEdgesAsync(p.Lat, p.Lon, version, radius, ct))
            {
                if (rejected.Contains(e.Id) || !evaluator.Allowed(e, t, out _)) continue;

                // An edge inside a closed zone (a pedestrian centre whose ways in are all
                // "private") is legal on its own but cannot be reached: A* would search the
                // whole graph, give up, and the request would fall back to OSRM, which does
                // not know the truck rules. The next nearest reachable edge is used instead.
                if (!await ReachesNetworkAsync(graph, e, t, ct))
                {
                    rejected.Add(e.Id);
                    logger.LogInformation("Snap skipped edge {EdgeId} ({Highway} {Name}): closed off for this truck.", e.Id, e.Highway, e.Name);
                    continue;
                }

                var input = new Point(p.Lon, p.Lat)
                {
                    SRID = 4326
                };
                var line = (LineString)e.Geometry;
                var lil = new LengthIndexedLine(line);
                var idx = lil.Project(input.Coordinate);
                var coord = lil.ExtractPoint(idx);
                var snapped = new Point(coord)
                {
                    SRID = 4326
                };
                // idx and line.Length are planar lengths in degrees, so they only
                // serve to get the fraction along the edge. Distances handed to the
                // router must be metres (edge.LengthM), otherwise the partial cost
                // of the first/last edge is ~0, which is wrong for long edges.
                var frac = line.Length <= 0 ? 0 : Math.Clamp(idx / line.Length, 0d, 1d);
                return new SnapResult(e.Id, e.SourceNode, e.TargetNode, input, snapped, DistanceMeters(input, snapped),
                    frac * e.LengthM, (1d - frac) * e.LengthM,
                    e.LengthM, frac, e.Direction >= 0, e.Direction <= 0);
            }
        }
        return null;
    }

    /// <summary>
    /// Breadth-first search over the edges this truck may use, from both ends of the edge.
    /// Outgoing options follow one-way rules, so this tests the way out of the edge; zones
    /// closed by access rules are closed in both directions.
    /// </summary>
    private async Task<bool> ReachesNetworkAsync(RoutingGraphCache.RoutingGraphView graph, RoadEdge edge, TruckProfile truck, CancellationToken ct)
    {
        var coordinates = new Dictionary<long, Coordinate>();
        PostGisAStarRouter.RegisterEdgeCoordinates(edge, coordinates);
        var seen = new HashSet<long> { edge.SourceNode, edge.TargetNode };
        var queue = new Queue<long>(seen);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!coordinates.TryGetValue(node, out var coordinate)) continue;
            foreach (var (next, forward) in await graph.GetOutgoingAsync(node, coordinate, ct))
            {
                if (!evaluator.Allowed(next, truck, out _)) continue;
                PostGisAStarRouter.RegisterEdgeCoordinates(next, coordinates);
                var other = forward ? next.TargetNode : next.SourceNode;
                if (!seen.Add(other)) continue;
                if (seen.Count >= ConnectedJunctions) return true;
                queue.Enqueue(other);
            }
        }

        return false;
    }

    static double DistanceMeters(Point a, Point b)
    {
        const double R = 6371000;
        var p1 = a.Y * Math.PI / 180;
        var p2 = b.Y * Math.PI / 180;
        var dp = (b.Y - a.Y) * Math.PI / 180;
        var dl = (b.X - a.X) * Math.PI / 180;
        var x = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(1 - x));
    }
}