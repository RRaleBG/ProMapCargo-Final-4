namespace ProMapCargo.OfflineRouting;

public sealed record OfflineManeuver(
    int Index,
    string Type,
    string Instruction,
    double DistanceFromPreviousMeters,
    double DistanceFromStartMeters,
    double Latitude,
    double Longitude,
    string? RoadName,
    string? RoadRef);

public sealed record OfflineRoute(
    double DistanceMeters,
    double DurationSeconds,
    IReadOnlyList<(double Lat, double Lon)> Points,
    IReadOnlyList<OfflineManeuver> Maneuvers,
    string StartSnap,
    string EndSnap,
    int SettledNodes);

/// <summary>
/// Truck routing on the phone over a <see cref="GraphBundle"/>: snaps start and
/// destination to the nearest drivable road, runs a bidirectional Contraction
/// Hierarchies search and turns the result into geometry and Serbian maneuvers
/// (same rules as the server's PostGIS router). One instance is not thread-safe.
/// </summary>
public sealed class OfflineRouter
{
    private const double MaxSnapMeters = 3000;

    private readonly GraphBundle graph;
    private readonly int[] forwardDistance;
    private readonly int[] backwardDistance;
    private readonly int[] forwardParentArc;
    private readonly int[] backwardParentArc;
    private readonly int[] forwardParentNode;
    private readonly int[] backwardParentNode;
    private readonly List<int> touched = [];
    private readonly List<(int Lat, int Lon)> pointBuffer = [];

    public OfflineRouter(GraphBundle graph)
    {
        this.graph = graph;
        var n = graph.NodeCount;
        forwardDistance = new int[n];
        backwardDistance = new int[n];
        forwardParentArc = new int[n];
        backwardParentArc = new int[n];
        forwardParentNode = new int[n];
        backwardParentNode = new int[n];
        Array.Fill(forwardDistance, int.MaxValue);
        Array.Fill(backwardDistance, int.MaxValue);
    }

    public GraphBundle Graph => graph;

    public OfflineRoute? Route(double startLat, double startLon, double destinationLat, double destinationLon)
    {
        var start = Snap(startLat, startLon);
        var end = Snap(destinationLat, destinationLon);
        if (start is null || end is null)
        {
            return null;
        }

        var (sources, targets) = (EntryNodes(start.Value), ExitNodes(end.Value));
        var best = Search(sources, targets, out var meeting, out var settled);

        // Start and destination on the same road segment, reachable directly.
        var direct = DirectCost(start.Value, end.Value);
        if (direct is { } directCost && directCost <= best)
        {
            ResetSearch();
            return Build(start.Value, end.Value, null, settled);
        }

        if (meeting < 0)
        {
            ResetSearch();
            return null;
        }

        var arcs = CollectPath(meeting, out var firstNode, out var lastNode);
        ResetSearch();

        var traversals = new List<(int Road, bool Backward)>();
        foreach (var arc in arcs)
        {
            Unpack(arc, traversals);
        }

        return Build(start.Value, end.Value, (traversals, firstNode, lastNode), settled);
    }

    // ---- Snapping ----

    public readonly record struct SnapPoint(int Road, double Along, double Length, double Lat, double Lon, double DistanceMeters);

    public SnapPoint? Snap(double lat, double lon)
    {
        SnapPoint? best = null;
        var cellRow = (int)Math.Floor((lat - graph.GridMinLat) / graph.GridCellDegrees);
        var cellCol = (int)Math.Floor((lon - graph.GridMinLon) / graph.GridCellDegrees);
        var seen = new HashSet<int>();

        // Rings of cells outward until a road is found (and one more ring to be safe).
        for (var ring = 0; ring <= 30; ring++)
        {
            for (var row = cellRow - ring; row <= cellRow + ring; row++)
            {
                for (var col = cellCol - ring; col <= cellCol + ring; col++)
                {
                    if (Math.Max(Math.Abs(row - cellRow), Math.Abs(col - cellCol)) != ring ||
                        row < 0 || col < 0 || row >= graph.GridRows || col >= graph.GridColumns)
                    {
                        continue;
                    }

                    var cell = row * graph.GridColumns + col;
                    for (var i = graph.GridOffsets[cell]; i < graph.GridOffsets[cell + 1]; i++)
                    {
                        var road = graph.GridRoads[i];
                        if (!seen.Add(road))
                        {
                            continue;
                        }

                        var candidate = ProjectOnRoad(road, lat, lon);
                        if (best is null || candidate.DistanceMeters < best.Value.DistanceMeters)
                        {
                            best = candidate;
                        }
                    }
                }
            }

            var ringMeters = ring * graph.GridCellDegrees * 111_000 * Math.Cos(lat * Math.PI / 180);
            if (best is not null && best.Value.DistanceMeters < ringMeters)
            {
                break;
            }
        }

        return best is { } found && found.DistanceMeters <= MaxSnapMeters ? found : null;
    }

    private SnapPoint ProjectOnRoad(int road, double lat, double lon)
    {
        graph.ReadRoadPoints(road, pointBuffer);
        var cosLat = Math.Cos(lat * Math.PI / 180);
        var bestDistance = double.MaxValue;
        var bestAlong = 0.0;
        double bestLat = lat, bestLon = lon;
        var along = 0.0;

        for (var i = 0; i < pointBuffer.Count - 1; i++)
        {
            var (aLat, aLon) = (pointBuffer[i].Lat / 1e6, pointBuffer[i].Lon / 1e6);
            var (bLat, bLon) = (pointBuffer[i + 1].Lat / 1e6, pointBuffer[i + 1].Lon / 1e6);

            // Local metric plane around the query point.
            var ax = (aLon - lon) * 111_320 * cosLat;
            var ay = (aLat - lat) * 110_540;
            var bx = (bLon - lon) * 111_320 * cosLat;
            var by = (bLat - lat) * 110_540;
            var dx = bx - ax;
            var dy = by - ay;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);
            var t = segmentLength > 0 ? Math.Clamp(-(ax * dx + ay * dy) / (segmentLength * segmentLength), 0, 1) : 0;
            var px = ax + dx * t;
            var py = ay + dy * t;
            var distance = Math.Sqrt(px * px + py * py);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAlong = along + segmentLength * t;
                bestLat = aLat + (bLat - aLat) * t;
                bestLon = aLon + (bLon - aLon) * t;
            }

            along += segmentLength;
        }

        return new SnapPoint(road, bestAlong, Math.Max(along, 0.01), bestLat, bestLon, bestDistance);
    }

    // ---- Search ----

    private List<(int Node, int Cost)> EntryNodes(SnapPoint snap)
    {
        var result = new List<(int, int)>(2);
        var weight = graph.RoadWeight[snap.Road];
        var direction = graph.RoadDirection[snap.Road];
        if (direction >= 0)
        {
            result.Add((graph.RoadTo[snap.Road], (int)Math.Round((snap.Length - snap.Along) / snap.Length * weight)));
        }

        if (direction <= 0)
        {
            result.Add((graph.RoadFrom[snap.Road], (int)Math.Round(snap.Along / snap.Length * weight)));
        }

        return result;
    }

    private List<(int Node, int Cost)> ExitNodes(SnapPoint snap)
    {
        var result = new List<(int, int)>(2);
        var weight = graph.RoadWeight[snap.Road];
        var direction = graph.RoadDirection[snap.Road];
        if (direction >= 0)
        {
            result.Add((graph.RoadFrom[snap.Road], (int)Math.Round(snap.Along / snap.Length * weight)));
        }

        if (direction <= 0)
        {
            result.Add((graph.RoadTo[snap.Road], (int)Math.Round((snap.Length - snap.Along) / snap.Length * weight)));
        }

        return result;
    }

    private int? DirectCost(SnapPoint start, SnapPoint end)
    {
        if (start.Road != end.Road)
        {
            return null;
        }

        var direction = graph.RoadDirection[start.Road];
        var forward = end.Along >= start.Along;
        if ((forward && direction < 0) || (!forward && direction > 0))
        {
            return null;
        }

        return (int)Math.Round(Math.Abs(end.Along - start.Along) / start.Length * graph.RoadWeight[start.Road]);
    }

    private int Search(List<(int Node, int Cost)> sources, List<(int Node, int Cost)> targets, out int meeting, out int settled)
    {
        var forwardQueue = new PriorityQueue<int, int>();
        var backwardQueue = new PriorityQueue<int, int>();

        foreach (var (node, cost) in sources)
        {
            if (cost < forwardDistance[node])
            {
                Touch(node);
                forwardDistance[node] = cost;
                forwardParentArc[node] = -1;
                forwardParentNode[node] = -1;
                forwardQueue.Enqueue(node, cost);
            }
        }

        foreach (var (node, cost) in targets)
        {
            if (cost < backwardDistance[node])
            {
                Touch(node);
                backwardDistance[node] = cost;
                backwardParentArc[node] = -1;
                backwardParentNode[node] = -1;
                backwardQueue.Enqueue(node, cost);
            }
        }

        var best = int.MaxValue;
        meeting = -1;
        settled = 0;

        while (forwardQueue.Count > 0 || backwardQueue.Count > 0)
        {
            var forwardMin = forwardQueue.TryPeek(out _, out var fp) ? fp : int.MaxValue;
            var backwardMin = backwardQueue.TryPeek(out _, out var bp) ? bp : int.MaxValue;
            if (Math.Min(forwardMin, backwardMin) >= best)
            {
                break;
            }

            var forwardTurn = forwardMin <= backwardMin;
            var queue = forwardTurn ? forwardQueue : backwardQueue;
            queue.TryDequeue(out var node, out var priority);
            var distances = forwardTurn ? forwardDistance : backwardDistance;
            if (priority > distances[node])
            {
                continue;
            }

            settled++;

            var other = forwardTurn ? backwardDistance[node] : forwardDistance[node];
            if (other != int.MaxValue && priority + other < best)
            {
                best = priority + other;
                meeting = node;
            }

            var offsets = forwardTurn ? graph.UpOffsets : graph.DownOffsets;
            var neighbours = forwardTurn ? graph.UpNode : graph.DownNode;
            var weights = forwardTurn ? graph.UpWeight : graph.DownWeight;
            var arcIds = forwardTurn ? graph.UpArc : graph.DownArc;
            var parentArc = forwardTurn ? forwardParentArc : backwardParentArc;
            var parentNode = forwardTurn ? forwardParentNode : backwardParentNode;

            for (var i = offsets[node]; i < offsets[node + 1]; i++)
            {
                var next = neighbours[i];
                var candidate = priority + weights[i];
                if (candidate < distances[next])
                {
                    Touch(next);
                    distances[next] = candidate;
                    parentArc[next] = arcIds[i];
                    parentNode[next] = node;
                    queue.Enqueue(next, candidate);
                }
            }
        }

        return best;
    }

    private void Touch(int node)
    {
        if (forwardDistance[node] == int.MaxValue && backwardDistance[node] == int.MaxValue)
        {
            touched.Add(node);
        }
    }

    private void ResetSearch()
    {
        foreach (var node in touched)
        {
            forwardDistance[node] = int.MaxValue;
            backwardDistance[node] = int.MaxValue;
        }

        touched.Clear();
    }

    private List<int> CollectPath(int meeting, out int firstNode, out int lastNode)
    {
        var forwardArcs = new List<int>();
        var node = meeting;
        while (forwardParentArc[node] >= 0)
        {
            forwardArcs.Add(forwardParentArc[node]);
            node = forwardParentNode[node];
        }

        firstNode = node;
        forwardArcs.Reverse();

        node = meeting;
        while (backwardParentArc[node] >= 0)
        {
            forwardArcs.Add(backwardParentArc[node]);
            node = backwardParentNode[node];
        }

        lastNode = node;
        return forwardArcs;
    }

    private void Unpack(int arc, List<(int Road, bool Backward)> output)
    {
        // Iterative to keep deep shortcut trees off the call stack.
        var stack = new Stack<int>();
        stack.Push(arc);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var a = graph.ArcA[current];
            if (a < 0)
            {
                var code = -a - 1;
                output.Add((code >> 1, (code & 1) == 1));
                continue;
            }

            stack.Push(graph.ArcB[current]);
            stack.Push(a);
        }
    }

    // ---- Result ----

    private OfflineRoute Build(SnapPoint start, SnapPoint end, (List<(int Road, bool Backward)> Roads, int FirstNode, int LastNode)? path, int settled)
    {
        // Each leg: road, travelled from 'fromAlong' to 'toAlong' (metres on the road).
        var legs = new List<(int Road, double FromAlong, double ToAlong)>();

        if (path is null)
        {
            legs.Add((start.Road, start.Along, end.Along));
        }
        else
        {
            var (roads, firstNode, lastNode) = path.Value;
            legs.Add((start.Road, start.Along, firstNode == graph.RoadTo[start.Road] ? start.Length : 0));
            foreach (var (road, backward) in roads)
            {
                var length = RoadGeometryLength(road);
                legs.Add(backward ? (road, length, 0) : (road, 0, length));
            }

            legs.Add((end.Road, lastNode == graph.RoadFrom[end.Road] ? 0 : end.Length, end.Along));
        }

        var points = new List<(double Lat, double Lon)>();
        var legPoints = new List<List<(double Lat, double Lon)>>();
        double distance = 0, duration = 0;

        foreach (var (road, fromAlong, toAlong) in legs)
        {
            var slice = Slice(road, fromAlong, toAlong);
            legPoints.Add(slice);
            var length = Math.Abs(toAlong - fromAlong);
            distance += length;
            duration += length / Math.Max(RoadGeometryLength(road), 0.01) * graph.RoadWeight[road] / 10.0;

            foreach (var point in slice)
            {
                if (points.Count == 0 || points[^1] != point)
                {
                    points.Add(point);
                }
            }
        }

        var maneuvers = BuildManeuvers(legs, legPoints, end);
        return new OfflineRoute(
            distance,
            duration,
            points,
            maneuvers,
            $"{start.DistanceMeters:0} m od puta",
            $"{end.DistanceMeters:0} m od puta",
            settled);
    }

    private double RoadGeometryLength(int road)
    {
        graph.ReadRoadPoints(road, pointBuffer);
        var length = 0.0;
        for (var i = 0; i < pointBuffer.Count - 1; i++)
        {
            length += Meters(pointBuffer[i], pointBuffer[i + 1]);
        }

        return Math.Max(length, 0.01);
    }

    private List<(double Lat, double Lon)> Slice(int road, double fromAlong, double toAlong)
    {
        graph.ReadRoadPoints(road, pointBuffer);
        var cumulative = new double[pointBuffer.Count];
        for (var i = 1; i < pointBuffer.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + Meters(pointBuffer[i - 1], pointBuffer[i]);
        }

        var forward = toAlong >= fromAlong;
        var low = Math.Min(fromAlong, toAlong);
        var high = Math.Max(fromAlong, toAlong);
        var result = new List<(double Lat, double Lon)> { PointAt(cumulative, low) };
        for (var i = 0; i < pointBuffer.Count; i++)
        {
            if (cumulative[i] > low && cumulative[i] < high)
            {
                result.Add((pointBuffer[i].Lat / 1e6, pointBuffer[i].Lon / 1e6));
            }
        }

        result.Add(PointAt(cumulative, high));
        if (!forward)
        {
            result.Reverse();
        }

        return result;
    }

    private (double Lat, double Lon) PointAt(double[] cumulative, double along)
    {
        for (var i = 0; i < cumulative.Length - 1; i++)
        {
            if (along <= cumulative[i + 1] || i == cumulative.Length - 2)
            {
                var span = cumulative[i + 1] - cumulative[i];
                var t = span > 0 ? Math.Clamp((along - cumulative[i]) / span, 0, 1) : 0;
                var a = pointBuffer[i];
                var b = pointBuffer[i + 1];
                return ((a.Lat + (b.Lat - a.Lat) * t) / 1e6, (a.Lon + (b.Lon - a.Lon) * t) / 1e6);
            }
        }

        return (pointBuffer[0].Lat / 1e6, pointBuffer[0].Lon / 1e6);
    }

    private List<OfflineManeuver> BuildManeuvers(
        List<(int Road, double FromAlong, double ToAlong)> legs,
        List<List<(double Lat, double Lon)>> legPoints,
        SnapPoint end)
    {
        var raw = new List<OfflineManeuver>();
        var cumulative = 0.0;

        for (var i = 0; i < legs.Count; i++)
        {
            var (road, fromAlong, toAlong) = legs[i];
            var length = Math.Abs(toAlong - fromAlong);
            var slice = legPoints[i];
            // Zero-length legs (snapped exactly onto a junction) carry no direction.
            if (slice.Count < 2 || length < 0.5)
            {
                cumulative += length;
                continue;
            }

            var type = raw.Count == 0 ? "Depart" : "Continue";
            if (raw.Count > 0 && FindPreviousSlice(legPoints, i) is { } previous)
            {
                var delta = ((Heading(slice[0], slice[1]) - Heading(previous[^2], previous[^1]) + 540) % 360) - 180;
                type = delta > 25 ? "Right" : delta < -25 ? "Left" : "Continue";
            }

            var name = graph.StringAt(graph.RoadName[road]);
            var reference = graph.StringAt(graph.RoadRef[road]);
            raw.Add(new OfflineManeuver(raw.Count, type, Instruction(type, name, reference), length, cumulative, slice[0].Lat, slice[0].Lon, name, reference));
            cumulative += length;
        }

        raw.Add(new OfflineManeuver(raw.Count, "Arrive", "Stigli ste", 0, cumulative, end.Lat, end.Lon, null, null));
        return Collapse(raw);
    }

    private static List<(double Lat, double Lon)>? FindPreviousSlice(List<List<(double Lat, double Lon)>> legPoints, int index)
    {
        for (var j = index - 1; j >= 0; j--)
        {
            if (legPoints[j].Count >= 2 && legPoints[j][0] != legPoints[j][^1])
            {
                return legPoints[j];
            }
        }

        return null;
    }

    private static string Instruction(string type, string? name, string? reference)
    {
        var road = string.IsNullOrWhiteSpace(name) ? reference : name;
        var on = string.IsNullOrWhiteSpace(road) ? string.Empty : $" na {road}";
        return type switch
        {
            "Depart" => $"Krenite{on}",
            "Left" => $"Skrenite levo{on}",
            "Right" => $"Skrenite desno{on}",
            _ => string.IsNullOrWhiteSpace(road) ? "Nastavite pravo" : $"Nastavite na {road}",
        };
    }

    /// <summary>Same as the server: straight segments on the same road fold into the step before.</summary>
    private static List<OfflineManeuver> Collapse(List<OfflineManeuver> raw)
    {
        var collapsed = new List<OfflineManeuver>(raw.Count);
        foreach (var maneuver in raw)
        {
            if (collapsed.Count > 0 && maneuver.Type == "Continue" && SameRoad(collapsed[^1], maneuver))
            {
                var previous = collapsed[^1];
                collapsed[^1] = previous with { DistanceFromPreviousMeters = previous.DistanceFromPreviousMeters + maneuver.DistanceFromPreviousMeters };
                continue;
            }

            collapsed.Add(maneuver with { Index = collapsed.Count });
        }

        return collapsed;
    }

    private static bool SameRoad(OfflineManeuver a, OfflineManeuver b)
    {
        var nameA = string.IsNullOrWhiteSpace(a.RoadName) ? a.RoadRef : a.RoadName;
        var nameB = string.IsNullOrWhiteSpace(b.RoadName) ? b.RoadRef : b.RoadName;
        return string.IsNullOrWhiteSpace(nameB) || string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase);
    }

    private static double Heading((double Lat, double Lon) a, (double Lat, double Lon) b)
    {
        var d = (b.Lon - a.Lon) * Math.PI / 180;
        var p1 = a.Lat * Math.PI / 180;
        var p2 = b.Lat * Math.PI / 180;
        return (Math.Atan2(Math.Sin(d) * Math.Cos(p2), Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(d)) * 180 / Math.PI + 360) % 360;
    }

    private static double Meters((int Lat, int Lon) a, (int Lat, int Lon) b)
    {
        var lat = (a.Lat + b.Lat) / 2e6 * Math.PI / 180;
        var dx = (b.Lon - a.Lon) / 1e6 * 111_320 * Math.Cos(lat);
        var dy = (b.Lat - a.Lat) / 1e6 * 110_540;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
