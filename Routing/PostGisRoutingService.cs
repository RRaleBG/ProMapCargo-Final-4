using Npgsql;
using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Routing;



public interface IPostGisRoutingService
{
    Task<RouteResponse> CalculateAsync(RouteRequest request, CancellationToken ct);
}



public sealed class PostGisRoutingService(PostGisRoutingRepository repo, EdgeSnapper snapper, PostGisAStarRouter
    router, ManeuverBuilder maneuvers) : IPostGisRoutingService
{
    /// <summary>
    /// Extract the coordinate at a fraction along a line that is closest to that fraction.
    /// For the first edge's start snap, this will extract from snap fraction to end.
    /// For the last edge's end snap, this will extract from start to snap point.
    /// </summary>
    static List<double[]> ExtractPointNear(NetTopologySuite.Geometries.LineString line, double fraction, bool isStartSnap)
    {
        var coords = line.Coordinates;
        if (coords.Length < 1)
            return new();

        // Clamp fraction to valid range
        fraction = Math.Max(0, Math.Min(1, fraction));

        // Find the segment this fraction falls into
        var targetDistance = line.Length * fraction;
        var currentDistance = 0.0;

        for (int i = 0; i < coords.Length - 1; i++)
        {
            var segmentDistance = coords[i].Distance(coords[i + 1]);
            var nextDistance = currentDistance + segmentDistance;

            if (targetDistance >= currentDistance && targetDistance <= nextDistance)
            {
                // Interpolate between coords[i] and coords[i+1]
                var segmentFraction = segmentDistance > 0
                    ? (targetDistance - currentDistance) / segmentDistance
                    : 0;

                var interpolatedX = coords[i].X + segmentFraction * (coords[i + 1].X - coords[i].X);
                var interpolatedY = coords[i].Y + segmentFraction * (coords[i + 1].Y - coords[i].Y);

                if (isStartSnap)
                {
                    // For START snap: return interpolated point + all remaining coordinates
                    var result = new List<double[]> { new[] { interpolatedX, interpolatedY } };
                    for (int j = i + 1; j < coords.Length; j++)
                    {
                        result.Add(new[] { coords[j].X, coords[j].Y });
                    }
                    return result;
                }
                else
                {
                    // For END snap: return all coordinates from start TO interpolated point
                    var result = new List<double[]>();
                    for (int j = 0; j <= i; j++)
                    {
                        result.Add(new[] { coords[j].X, coords[j].Y });
                    }
                    result.Add(new[] { interpolatedX, interpolatedY });
                    return result;
                }
            }

            currentDistance = nextDistance;
        }

        // Fallback
        if (isStartSnap)
            return new() { new[] { coords[^1].X, coords[^1].Y } };
        else
            return coords.Select(c => new[] { c.X, c.Y }).ToList();
    }



    public async Task<RouteResponse> CalculateAsync(RouteRequest request, CancellationToken ct)
    {
        Console.WriteLine($"[ROUTING] CalculateAsync START - Start:[{request.Start.Lat:F6}, {request.Start.Lon:F6}], Target:[{request.Target.Lat:F6}, {request.Target.Lon:F6}]");

        var truck = request.Truck ?? new TruckProfile
        {
            GrossWeightTons = 40,
            HeightMeters = 4,
            WidthMeters = 2.55m,
            LengthMeters = 16.5m
        }
        ;

        try
        {
            var canConnect = await repo.CanConnectAsync(ct);
            if (!canConnect)
            {
                Console.WriteLine("[ROUTING] ERROR: PostGIS connectivity probe failed");
                return new RouteResponse
                {
                    Code = "RoutingError",
                    Message = "PostGIS database is not reachable.",
                    IsTruckSafe = false,
                    Diagnostics = new RouteDiagnostics
                    {
                        Engine = "PostGIS-AStar",
                        UsedFallback = false,
                        FailureReason = "PostGIS database is not reachable."
                    }
                };
            }
        }
        catch (NpgsqlException ex)
        {
            Console.WriteLine($"[ROUTING] ERROR: PostGIS connection failed: {ex.Message}");
            return new RouteResponse
            {
                Code = "RoutingError",
                Message = "PostGIS database connection failed.",
                IsTruckSafe = false,
                Diagnostics = new RouteDiagnostics
                {
                    Engine = "PostGIS-AStar",
                    UsedFallback = false,
                    FailureReason = "PostGIS database connection failed."
                }
            };
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"[ROUTING] ERROR: PostGIS connection timeout: {ex.Message}");
            return new RouteResponse
            {
                Code = "RoutingError",
                Message = "PostGIS database connection timed out.",
                IsTruckSafe = false,
                Diagnostics = new RouteDiagnostics
                {
                    Engine = "PostGIS-AStar",
                    UsedFallback = false,
                    FailureReason = "PostGIS database connection timed out."
                }
            };
        }

        var version = await repo.GetActiveGraphVersionAsync(ct);
        if (version is null)
        {
            Console.WriteLine($"[ROUTING] ERROR: No active graph version found");
            return new RouteResponse
            {
                Code = "NoGraph",
                Message = "No active graph version found.",
                IsTruckSafe = false,
                Diagnostics = new RouteDiagnostics
                {
                    Engine = "PostGIS-AStar",
                    UsedFallback = false,
                    FailureReason = "No active graph version found."
                }
            };
        }
        var s = await snapper.SnapAsync(request.Start, truck, version.Value, ct);
        var e = await snapper.SnapAsync(request.Target, truck, version.Value, ct);

        if (s is not null)
            Console.WriteLine($"[ROUTING] Start snap: EdgeId={s.EdgeId}, Fraction={s.Fraction:F4}, Distance={s.DistanceToEdgeM:F1}m");
        else
            Console.WriteLine($"[ROUTING] ERROR: Start snap failed");

        if (e is not null)
            Console.WriteLine($"[ROUTING] Target snap: EdgeId={e.EdgeId}, Fraction={e.Fraction:F4}, Distance={e.DistanceToEdgeM:F1}m");
        else
            Console.WriteLine($"[ROUTING] ERROR: Target snap failed");

        return new RouteResponse
        {
            Code = "SnapFailed",
            Message = s is null && e is null
                ? "Start and destination could not be snapped to the active graph."
                : s is null
                    ? "Start could not be snapped to the active graph."
                    : "Destination could not be snapped to the active graph.",
            IsTruckSafe = false,
            Diagnostics = new RouteDiagnostics
            {
                Engine = "PostGIS-AStar",
                UsedFallback = false,
                GraphVersion = version,
                FailureReason = s is null && e is null
                    ? "Start and destination could not be snapped to the active graph."
                    : s is null
                        ? "Start could not be snapped to the active graph."
                        : "Destination could not be snapped to the active graph."
            }
        }
        ;
        var r = await router.RouteAsync(s, e, truck, version.Value, ct);

        if (!r.Success)
        {
            Console.WriteLine($"[ROUTING] ERROR: Routing failed - {r.FailureReason}");
            return new RouteResponse
            {
                Code = "NoPath",
                Message = r.FailureReason,
                IsTruckSafe = false,
                Diagnostics = new RouteDiagnostics
                {
                    Engine = r.Engine,
                    UsedFallback = false,
                    ExpandedStates = r.ExpandedStates,
                    GraphVersion = version,
                    FailureReason = r.FailureReason,
                    StartSnap = r.StartSnap,
                    EndSnap = r.EndSnap,
                    TraversalCount = r.Traversals.Count,
                    Highlights = r.DebugHighlights.ToList()
                }
            };
        }

        Console.WriteLine($"[ROUTING] Routing SUCCESS: {r.Traversals.Count} traversals, Distance={r.DistanceM:F0}m, Duration={r.DurationS:F0}s");

        var edgeIds = r.Traversals.Select(x => x.EdgeId).ToArray();
        var edges = await repo.GetEdgesByIdsAsync(edgeIds, version.Value, ct);
        var byId = edges.ToDictionary(x => x.Id);

        var geometry = RouteGeometryBuilder.Build(r.Traversals, byId, s, e);

        Console.WriteLine(
            $"[GEOMETRY] original={geometry.OriginalPointCount} oriented={geometry.OrientedPointCount} trimmed={geometry.TrimmedPointCount} final={geometry.FinalPointCount}");

        if (!geometry.Success)
        {
            Console.WriteLine($"[ROUTING] ERROR: Invalid route geometry - {geometry.FailureReason}");

            return new RouteResponse
            {
                Code = "InvalidRouteGeometry",
                Message = geometry.FailureReason,
                IsTruckSafe = false,
                Diagnostics = new RouteDiagnostics
                {
                    Engine = r.Engine,
                    UsedFallback = false,
                    ExpandedStates = r.ExpandedStates,
                    GraphVersion = version,
                    FailureReason = geometry.FailureReason,
                    StartSnap = r.StartSnap,
                    EndSnap = r.EndSnap,
                    TraversalCount = r.Traversals.Count,
                    Highlights = r.DebugHighlights.ToList()
                }
            };
        }

        var coordinates = geometry.Coordinates.ToList();
        var geo = new Dictionary<string, object?>
        {
            ["type"] = "LineString",
            ["coordinates"] = coordinates
        };

        var startSnap = s is null || !byId.TryGetValue(s.EdgeId, out var startEdge)
            ? null
            : $"edge {startEdge.Id} · {(string.IsNullOrWhiteSpace(startEdge.Name) ? startEdge.Highway : startEdge.Name)}{(string.IsNullOrWhiteSpace(startEdge.Ref) ? string.Empty : $" [{startEdge.Ref}]")} · offset {s.DistanceToEdgeM:F0} m · fraction {s.Fraction:F3}";

        var endSnap = e is null || !byId.TryGetValue(e.EdgeId, out var endEdge)
            ? null
            : $"edge {endEdge.Id} · {(string.IsNullOrWhiteSpace(endEdge.Name) ? endEdge.Highway : endEdge.Name)}{(string.IsNullOrWhiteSpace(endEdge.Ref) ? string.Empty : $" [{endEdge.Ref}]")} · offset {e.DistanceToEdgeM:F0} m · fraction {e.Fraction:F3}";

        var routeHighlights = r.Traversals.Select(tr => byId.TryGetValue(tr.EdgeId, out var edge)
                ? $"{(string.IsNullOrWhiteSpace(edge.Name) ? edge.Highway : edge.Name)}{(string.IsNullOrWhiteSpace(edge.Ref) ? string.Empty : $" [{edge.Ref}]")}: {tr.DistanceM:F0} m / {tr.DurationS:F0} s"
                : $"edge {tr.EdgeId}: {tr.DistanceM:F0} m / {tr.DurationS:F0} s")
            .Take(12)
            .ToList();

        var candidate = new RouteCandidate
        {
            Distance = r.DistanceM,
            Duration = r.DurationS,
            Geometry = geo,
            Legs = null,
            Analysis = new RouteAnalysis
            {
                Restricted = false,
                Score = 0,
                Violations = [],
                Debug = new RouteDebug
                {
                    Summary = $"{r.Traversals.Count} traversals through PostGIS truck graph.",
                    StartSnap = r.StartSnap ?? startSnap,
                    EndSnap = r.EndSnap ?? endSnap,
                    TraversalCount = r.Traversals.Count,
                    Highlights = routeHighlights
                }
            }
        };

        Console.WriteLine($"[ROUTING] CalculateAsync COMPLETE - Returning Ok with {r.DistanceM:F0}m route");

        var maneuverDtos = maneuvers.Build(r.Traversals, edges);

        return new RouteResponse
        {
            Code = "Ok",
            Message = "PostGIS route calculated.",
            Routes = [candidate],
            SelectedRouteIndex = 0,
            IsTruckSafe = true,
            Violations = [],
            Summary = new RouteSummary(r.DistanceM, r.DurationS, (request.DepartureAt ?? DateTimeOffset.UtcNow).AddSeconds(r.DurationS)),
            Diagnostics = new RouteDiagnostics
            {
                Engine = r.Engine,
                UsedFallback = false,
                ExpandedStates = r.ExpandedStates,
                GraphVersion = version,
                FailureReason = r.FailureReason,
                StartSnap = r.StartSnap ?? startSnap,
                EndSnap = r.EndSnap ?? endSnap,
                TraversalCount = r.Traversals.Count,
                Highlights = routeHighlights
            },
            Maneuvers = maneuverDtos.ToList()
        };
    }
}
