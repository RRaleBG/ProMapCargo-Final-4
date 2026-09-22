using NetTopologySuite.Geometries;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Routing;

public sealed class PostGisAStarRouter(
    PostGisRoutingRepository repo,
    TruckEdgeEvaluator evaluator,
    TurnRestrictionMatcher restrictions)
{
    private const int MaxExpandedStates = 1_000_000;

    private readonly record struct SearchState(
        long Node,
        long? PreviousWay);

    private sealed record PreviousEntry(
        SearchState Previous,
        RoutedTraversal Traversal);

    public async Task<PostGisRouteResult> RouteAsync(
        SnapResult start,
        SnapResult end,
        TruckProfile truck,
        long version,
        CancellationToken ct)
    {
        var startEdgeIds =
            start.EdgeId == end.EdgeId
                ? new[] { start.EdgeId }
                : new[] { start.EdgeId, end.EdgeId };

        var terminalEdges =
            (await repo.GetEdgesByIdsAsync(
                startEdgeIds,
                version,
                ct))
            .ToDictionary(x => x.Id);

        if (!terminalEdges.TryGetValue(start.EdgeId, out var startEdge))
        {
            return new PostGisRouteResult(
                false,
                [],
                0,
                0,
                0,
                "PostGIS-AStar",
                "Start edge not found.",
                [],
                null,
                null);
        }

        if (!terminalEdges.TryGetValue(end.EdgeId, out var endEdge))
        {
            return new PostGisRouteResult(
                false,
                [],
                0,
                0,
                0,
                "PostGIS-AStar",
                "Destination edge not found.",
                [],
                null,
                null);
        }

        /*
         * ------------------------------------------------------------
         * 1. START == END EDGE
         * ------------------------------------------------------------
         */

        if (start.EdgeId == end.EdgeId)
        {
            var directCandidates =
                new List<RoutedTraversal>();

            var startSnap = BuildSnapDebug(start, startEdge);
            var endSnap = BuildSnapDebug(end, endEdge);

            if (end.Fraction >= start.Fraction &&
                start.CanTravelForward &&
                evaluator.Allowed(startEdge, truck, out _))
            {
                var distance =
                    Math.Max(
                        0,
                        end.Fraction - start.Fraction)
                    * start.EdgeLengthM;

                var duration =
                    DurationSeconds(
                        distance,
                        evaluator.Speed(startEdge, truck));

                directCandidates.Add(
                    new RoutedTraversal(
                        startEdge.Id,
                        startEdge.SourceNode,
                        startEdge.TargetNode,
                        true,
                        distance,
                        duration));
            }

            if (end.Fraction <= start.Fraction &&
                start.CanTravelReverse &&
                evaluator.Allowed(startEdge, truck, out _))
            {
                var distance =
                    Math.Max(
                        0,
                        start.Fraction - end.Fraction)
                    * start.EdgeLengthM;

                var duration =
                    DurationSeconds(
                        distance,
                        evaluator.Speed(startEdge, truck));

                directCandidates.Add(
                    new RoutedTraversal(
                        startEdge.Id,
                        startEdge.SourceNode,
                        startEdge.TargetNode,
                        false,
                        distance,
                        duration));
            }

            if (directCandidates.Count == 0)
            {
                return new PostGisRouteResult(
                    false,
                    [],
                    0,
                    0,
                    1,
                    "PostGIS-AStar",
                    "No valid direction exists between start and destination on the same edge.",
                    [],
                    startSnap,
                    endSnap);
            }

            var direct =
                directCandidates
                    .OrderBy(x => x.DurationS)
                    .First();

            return new PostGisRouteResult(
                true,
                new[] { direct },
                direct.DistanceM,
                direct.DurationS,
                1,
                "PostGIS-AStar",
                null,
                [BuildTraversalHighlight(startEdge, direct.DistanceM, direct.DurationS)],
                startSnap,
                endSnap);
        }

        /*
         * ------------------------------------------------------------
         * 2. RESTRICTIONS
         * ------------------------------------------------------------
         */

        var restrictionSet =
            await restrictions.LoadAsync(
                version,
                ct);

        /*
         * ------------------------------------------------------------
         * 3. A* STATE
         *
         * State = node + previous way.
         *
         * previous way is required for turn restrictions.
         * ------------------------------------------------------------
         */

        var dist =
            new Dictionary<SearchState, double>();

        var prev =
            new Dictionary<SearchState, PreviousEntry>();

        /*
         * Initial partial traversals from the snapped start point.
         *
         * We start in the graph at either endpoint of the snapped
         * start edge, depending on legal travel direction.
         */

        var seedTraversals =
            new Dictionary<SearchState, RoutedTraversal>();

        var startSpeed =
            evaluator.Speed(
                startEdge,
                truck);

        if (!evaluator.Allowed(
                startEdge,
                truck,
                out _))
        {
            return new PostGisRouteResult(
                false,
                [],
                0,
                0,
                0,
                "PostGIS-AStar",
                "Start edge is not legal for this truck profile.",
                [],
                BuildSnapDebug(start, startEdge),
                BuildSnapDebug(end, endEdge));
        }

        if (start.CanTravelForward)
        {
            var distance =
                Math.Max(
                    0,
                    1d - start.Fraction)
                * start.EdgeLengthM;

            var duration =
                DurationSeconds(
                    distance,
                    startSpeed);

            var state =
                new SearchState(
                    startEdge.TargetNode,
                    startEdge.WayId);

            var traversal =
                new RoutedTraversal(
                    startEdge.Id,
                    startEdge.SourceNode,
                    startEdge.TargetNode,
                    true,
                    distance,
                    duration);

            AddSeed(
                state,
                distance,
                traversal,
                dist,
                seedTraversals);
        }

        if (start.CanTravelReverse)
        {
            var distance =
                Math.Max(
                    0,
                    start.Fraction)
                * start.EdgeLengthM;

            var duration =
                DurationSeconds(
                    distance,
                    startSpeed);

            var state =
                new SearchState(
                    startEdge.SourceNode,
                    startEdge.WayId);

            var traversal =
                new RoutedTraversal(
                    startEdge.Id,
                    startEdge.SourceNode,
                    startEdge.TargetNode,
                    false,
                    distance,
                    duration);

            AddSeed(
                state,
                distance,
                traversal,
                dist,
                seedTraversals);
        }

        if (dist.Count == 0)
        {
            return new PostGisRouteResult(
                false,
                [],
                0,
                0,
                0,
                "PostGIS-AStar",
                "Start edge has no legal travel direction.",
                [],
                BuildSnapDebug(start, startEdge),
                BuildSnapDebug(end, endEdge));
        }

        /*
         * Node coordinates are derived from edge geometries so A*
         * can calculate a straight-line lower-bound heuristic without
         * an additional database query for every node.
         */

        var nodeCoordinates =
            new Dictionary<long, Coordinate>();

        RegisterEdgeCoordinates(
            startEdge,
            nodeCoordinates);

        RegisterEdgeCoordinates(
            endEdge,
            nodeCoordinates);

        var queue =
            new PriorityQueue<SearchState, double>();

        var maxTruckSpeedKmh =
            Math.Max(
                5d,
                (double)truck.MaxSpeedKmh);

        var goalPoint =
            end.SnappedPoint.Coordinate;

        foreach (var state in dist.Keys.ToArray())
        {
            var h =
                HeuristicSeconds(
                    state.Node,
                    nodeCoordinates,
                    goalPoint,
                    maxTruckSpeedKmh);

            queue.Enqueue(
                state,
                dist[state] + h);
        }

        var outgoingCache =
            new Dictionary<
                long,
                IReadOnlyList<(RoadEdge Edge, bool Forward)>>();

        SearchState? bestGoalState = null;
        RoutedTraversal? bestFinalTraversal = null;
        var bestGoalCost = double.PositiveInfinity;

        var expanded = 0;

        /*
         * ------------------------------------------------------------
         * 4. A* SEARCH
         * ------------------------------------------------------------
         */

        while (queue.Count > 0 &&
               expanded < MaxExpandedStates)
        {
            ct.ThrowIfCancellationRequested();

            queue.TryPeek(
                out _,
                out var peekPriority);

            /*
             * Once the smallest possible f-score is already worse
             * than the best complete solution, the current solution
             * is optimal.
             */

            if (bestGoalState is not null &&
                peekPriority >= bestGoalCost)
            {
                break;
            }

            var state =
                queue.Dequeue();

            if (!dist.TryGetValue(
                    state,
                    out var baseCost))
            {
                continue;
            }

            var currentPriority =
                baseCost +
                HeuristicSeconds(
                    state.Node,
                    nodeCoordinates,
                    goalPoint,
                    maxTruckSpeedKmh);

            /*
             * Ignore stale PriorityQueue entries.
             */

            if (currentPriority > peekPriority + 0.000001)
            {
                continue;
            }

            expanded++;

            /*
             * --------------------------------------------------------
             * Check whether we can finish on the destination edge.
             * --------------------------------------------------------
             */

            if (state.Node == endEdge.SourceNode &&
                end.CanTravelForward &&
                restrictionSet.Allows(
                    state.PreviousWay,
                    endEdge.WayId,
                    state.Node))
            {
                var distance =
                    Math.Max(
                        0,
                        end.Fraction)
                    * end.EdgeLengthM;

                var duration =
                    DurationSeconds(
                        distance,
                        evaluator.Speed(
                            endEdge,
                            truck));

                var total =
                    baseCost + duration;

                if (total < bestGoalCost)
                {
                    bestGoalCost = total;
                    bestGoalState = state;
                    bestFinalTraversal =
                        distance <= 0
                            ? null
                            : new RoutedTraversal(
                                endEdge.Id,
                                endEdge.SourceNode,
                                endEdge.TargetNode,
                                true,
                                distance,
                                duration);
                }
            }

            if (state.Node == endEdge.TargetNode &&
                end.CanTravelReverse &&
                restrictionSet.Allows(
                    state.PreviousWay,
                    endEdge.WayId,
                    state.Node))
            {
                var distance =
                    Math.Max(
                        0,
                        1d - end.Fraction)
                    * end.EdgeLengthM;

                var duration =
                    DurationSeconds(
                        distance,
                        evaluator.Speed(
                            endEdge,
                            truck));

                var total =
                    baseCost + duration;

                if (total < bestGoalCost)
                {
                    bestGoalCost = total;
                    bestGoalState = state;
                    bestFinalTraversal =
                        distance <= 0
                            ? null
                            : new RoutedTraversal(
                                endEdge.Id,
                                endEdge.SourceNode,
                                endEdge.TargetNode,
                                false,
                                distance,
                                duration);
                }
            }

            /*
             * --------------------------------------------------------
             * Expand graph node.
             * --------------------------------------------------------
             */

            if (!outgoingCache.TryGetValue(
                    state.Node,
                    out var outgoing))
            {
                outgoing =
                    await repo.GetOutgoingAsync(
                        state.Node,
                        version,
                        ct);

                outgoingCache[state.Node] =
                    outgoing;
            }

            foreach (var (edge, forward) in outgoing)
            {
                if (!evaluator.Allowed(
                        edge,
                        truck,
                        out _))
                {
                    continue;
                }

                if (!restrictionSet.Allows(
                        state.PreviousWay,
                        edge.WayId,
                        state.Node))
                {
                    continue;
                }

                var nextNode =
                    forward
                        ? edge.TargetNode
                        : edge.SourceNode;

                RegisterEdgeCoordinates(
                    edge,
                    nodeCoordinates);

                var speed =
                    evaluator.Speed(
                        edge,
                        truck);

                var seconds =
                    DurationSeconds(
                        edge.LengthM,
                        speed);

                var newDistance =
                    baseCost + seconds;

                var nextState =
                    new SearchState(
                        nextNode,
                        edge.WayId);

                if (dist.TryGetValue(
                        nextState,
                        out var oldDistance) &&
                    newDistance >= oldDistance)
                {
                    continue;
                }

                dist[nextState] =
                    newDistance;

                prev[nextState] =
                    new PreviousEntry(
                        state,
                        new RoutedTraversal(
                            edge.Id,
                            edge.SourceNode,
                            edge.TargetNode,
                            forward,
                            edge.LengthM,
                            seconds));

                /*
                 * This state is no longer a pure initial seed if a
                 * cheaper graph path has now reached it.
                 */

                seedTraversals.Remove(
                    nextState);

                var heuristic =
                    HeuristicSeconds(
                        nextNode,
                        nodeCoordinates,
                        goalPoint,
                        maxTruckSpeedKmh);

                queue.Enqueue(
                    nextState,
                    newDistance + heuristic);
            }
        }

        /*
         * ------------------------------------------------------------
         * 5. NO ROUTE
         * ------------------------------------------------------------
         */

        if (bestGoalState is null)
        {
            var reason =
                expanded >= MaxExpandedStates
                    ? $"A* expansion limit reached ({MaxExpandedStates:N0})."
                    : "Ruta nije pronađena.";

            return new PostGisRouteResult(
                false,
                [],
                0,
                0,
                expanded,
                "PostGIS-AStar",
                reason,
                [],
                BuildSnapDebug(start, startEdge),
                BuildSnapDebug(end, endEdge));
        }

        /*
         * ------------------------------------------------------------
         * 6. RECONSTRUCT ROUTE
         * ------------------------------------------------------------
         */

        var reversed =
            new List<RoutedTraversal>();

        var cursor =
            bestGoalState.Value;

        while (prev.TryGetValue(
                   cursor,
                   out var entry))
        {
            reversed.Add(
                entry.Traversal);

            cursor =
                entry.Previous;
        }

        if (seedTraversals.TryGetValue(
                cursor,
                out var seedTraversal))
        {
            reversed.Add(
                seedTraversal);
        }

        reversed.Reverse();

        if (bestFinalTraversal is not null)
        {
            reversed.Add(
                bestFinalTraversal);
        }

        if (reversed.Count == 0)
        {
            return new PostGisRouteResult(
                false,
                [],
                0,
                0,
                expanded,
                "PostGIS-AStar",
                "Route reconstruction returned no traversals.",
                [],
                BuildSnapDebug(start, startEdge),
                BuildSnapDebug(end, endEdge));
        }

        /*
         * ------------------------------------------------------------
         * 7. FINAL TOTALS
         * ------------------------------------------------------------
         */

        var totalDistance =
            reversed.Sum(
                x => x.DistanceM);

        var totalDuration =
            reversed.Sum(
                x => x.DurationS);

        var edgeLookup = terminalEdges;
        var highlights =
            reversed
                .Take(8)
                .Select(traversal =>
                {
                    if (edgeLookup.TryGetValue(traversal.EdgeId, out var edge))
                    {
                        return BuildTraversalHighlight(edge, traversal.DistanceM, traversal.DurationS);
                    }

                    return $"edge {traversal.EdgeId}: {traversal.DistanceM:F0} m / {traversal.DurationS:F0} s";
                })
                .ToList();

        return new PostGisRouteResult(
            true,
            reversed,
            totalDistance,
            totalDuration,
            expanded,
            "PostGIS-AStar",
            null,
            highlights,
            BuildSnapDebug(start, startEdge),
            BuildSnapDebug(end, endEdge));
    }

    private static void AddSeed(
        SearchState state,
        double distance,
        RoutedTraversal traversal,
        Dictionary<SearchState, double> dist,
        Dictionary<SearchState, RoutedTraversal> seeds)
    {
        if (!dist.TryGetValue(
                state,
                out var oldDistance) ||
            distance < oldDistance)
        {
            dist[state] = distance;
            seeds[state] = traversal;
        }
    }

    private static string BuildSnapDebug(
        SnapResult snap,
        RoadEdge edge)
    {
        var road = string.IsNullOrWhiteSpace(edge.Name)
            ? edge.Highway
            : edge.Name;

        var reference = string.IsNullOrWhiteSpace(edge.Ref)
            ? string.Empty
            : $" [{edge.Ref}]";

        return $"edge {edge.Id} · {road ?? "unknown"}{reference} · offset {snap.DistanceToEdgeM:F0} m · fraction {snap.Fraction:F3}";
    }

    private static string BuildTraversalHighlight(
        RoadEdge edge,
        double distanceMeters,
        double durationSeconds)
    {
        var road = string.IsNullOrWhiteSpace(edge.Name)
            ? edge.Highway
            : edge.Name;

        var reference = string.IsNullOrWhiteSpace(edge.Ref)
            ? string.Empty
            : $" [{edge.Ref}]";

        return $"{road ?? "unknown"}{reference}: {distanceMeters:F0} m / {durationSeconds:F0} s";
    }

    private static void RegisterEdgeCoordinates(
        RoadEdge edge,
        Dictionary<long, Coordinate> coordinates)
    {
        if (edge.Geometry is not LineString line ||
            line.Coordinates.Length < 2)
        {
            return;
        }

        coordinates[edge.SourceNode] =
            line.Coordinates[0];

        coordinates[edge.TargetNode] =
            line.Coordinates[^1];
    }

    private static double DurationSeconds(
        double distanceMeters,
        double speedKmh)
    {
        var speedMs =
            Math.Max(
                1d,
                speedKmh / 3.6);

        return distanceMeters / speedMs;
    }

    private static double HeuristicSeconds(
        long node,
        Dictionary<long, Coordinate> coordinates,
        Coordinate goal,
        double maxSpeedKmh)
    {
        if (!coordinates.TryGetValue(
                node,
                out var current))
        {
            return 0;
        }

        var distance =
            GreatCircleDistanceMeters(
                current.Y,
                current.X,
                goal.Y,
                goal.X);

        return DurationSeconds(
            distance,
            maxSpeedKmh);
    }

    private static double GreatCircleDistanceMeters(
        double lat1,
        double lon1,
        double lat2,
        double lon2)
    {
        const double EarthRadiusMeters = 6_371_000d;

        var p1 = lat1 * Math.PI / 180d;

        var p2 = lat2 * Math.PI / 180d;

        var dLat = (lat2 - lat1) * Math.PI / 180d;

        var dLon = (lon2 - lon1) * Math.PI / 180d;

        var a =
            Math.Sin(dLat / 2d) *
            Math.Sin(dLat / 2d) +
            Math.Cos(p1) *
            Math.Cos(p2) *
            Math.Sin(dLon / 2d) *
            Math.Sin(dLon / 2d);

        return EarthRadiusMeters * 2d *  Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0d, 1d - a)));
    }
}