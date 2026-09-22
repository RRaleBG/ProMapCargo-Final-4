using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Routing;
public interface IPostGisRoutingService {
    Task<RouteResponse> CalculateAsync(RouteRequest request,CancellationToken ct);
}
public sealed class PostGisRoutingService(PostGisRoutingRepository repo,EdgeSnapper snapper,PostGisAStarRouter
    router,ManeuverBuilder maneuvers):IPostGisRoutingService
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
    public async Task<RouteResponse> CalculateAsync(RouteRequest request,CancellationToken ct) {
        var truck=request.Truck??new TruckProfile {
            GrossWeightTons=40,HeightMeters=4,WidthMeters=2.55m,LengthMeters=16.5m
        }
        ;
        var version=await repo.GetActiveGraphVersionAsync(ct);
        if(version is null)return new RouteResponse {
            Code="NoGraph",IsTruckSafe=false
        }
        ;
        var s=await snapper.SnapAsync(request.Start,truck,version.Value,ct);
        var e=await snapper.SnapAsync(request.Target,truck,version.Value,ct);
        if(s is null||e is null)return new RouteResponse {
            Code="NoSnap",IsTruckSafe=false
        }
        ;
        var r=await router.RouteAsync(s,e,truck,version.Value,ct);
        if(!r.Success)return new RouteResponse {
            Code="NoRoute",IsTruckSafe=false
        }
        ;
        var edgeIds=r.Traversals.Select(x=>x.EdgeId).ToArray();
        var edges=await repo.GetEdgesByIdsAsync(edgeIds,version.Value,ct);
        var byId=edges.ToDictionary(x=>x.Id);
        var coordinates=new List<double[]>();

        // Debug: Check if start and end snap are on same edge
        if (s is not null && e is not null && s.EdgeId == e.EdgeId && r.Traversals.Count == 1)
        {
            Console.WriteLine($"[ROUTING] WARNING: Start and end snap on SAME edge! route will be very short. Start frac={s.Fraction:F4}, End frac={e.Fraction:F4}");
        }

        for (int traversalIdx = 0; traversalIdx < r.Traversals.Count; traversalIdx++)
        {
            var tr = r.Traversals[traversalIdx];
            if(!byId.TryGetValue(tr.EdgeId,out var edge))continue;
            var line=(NetTopologySuite.Geometries.LineString)edge.Geometry;

            List<double[]> pts;

            // On first edge: snap to start fraction if available
            if (traversalIdx == 0 && s is not null)
            {
                pts = ExtractPointNear(line, s.Fraction, isStartSnap: true);
                Console.WriteLine($"[ROUTING] First edge (id={edge.Id}): using start snap at fraction {s.Fraction:F4}, extracted {pts.Count} coordinates");
            }
            // On last edge: snap to end fraction if available
            else if (traversalIdx == r.Traversals.Count - 1 && e is not null)
            {
                pts = ExtractPointNear(line, e.Fraction, isStartSnap: false);
                Console.WriteLine($"[ROUTING] Last edge (id={edge.Id}): using end snap at fraction {e.Fraction:F4}, extracted {pts.Count} coordinates");
            }
            // Middle edges: use all coordinates
            else
            {
                pts=line.Coordinates.Select(c=>new[] {c.X,c.Y}).ToList();
                Console.WriteLine($"[ROUTING] Middle edge (id={edge.Id}): using all {pts.Count} coordinates");
            }

            if(!tr.Forward)pts.Reverse();
            if(coordinates.Count>0 && pts.Count > 0)pts.RemoveAt(0);
            coordinates.AddRange(pts);
        }

        Console.WriteLine($"[ROUTING] Final route has {coordinates.Count} total coordinates from {r.Traversals.Count} traversals");
        if(coordinates.Count<2)coordinates=[new[] {
            request.Start.Lon,request.Start.Lat
        }
        ,new[] {
            request.Target.Lon,request.Target.Lat
        }
        ];
        var geo=new Dictionary<string,object?> {
            {
                "type","LineString"
            }
            , {
                "coordinates",coordinates
            }
        }
        ;
        var startSnap = s is null || !byId.TryGetValue(s.EdgeId, out var startEdge)
            ? null
            : $"edge {startEdge.Id} · {(string.IsNullOrWhiteSpace(startEdge.Name) ? startEdge.Highway : startEdge.Name)}{(string.IsNullOrWhiteSpace(startEdge.Ref) ? string.Empty : $" [{startEdge.Ref}]")} · offset {s.DistanceToEdgeM:F0} m · fraction {s.Fraction:F3}";
        var endSnap = e is null || !byId.TryGetValue(e.EdgeId, out var endEdge)
            ? null
            : $"edge {endEdge.Id} · {(string.IsNullOrWhiteSpace(endEdge.Name) ? endEdge.Highway : endEdge.Name)}{(string.IsNullOrWhiteSpace(endEdge.Ref) ? string.Empty : $" [{endEdge.Ref}]")} · offset {e.DistanceToEdgeM:F0} m · fraction {e.Fraction:F3}";
        var routeHighlights=r.Traversals.Select(tr=>byId.TryGetValue(tr.EdgeId,out var edge)
                ? $"{(string.IsNullOrWhiteSpace(edge.Name) ? edge.Highway : edge.Name)}{(string.IsNullOrWhiteSpace(edge.Ref) ? string.Empty : $" [{edge.Ref}]")}: {tr.DistanceM:F0} m / {tr.DurationS:F0} s"
                : $"edge {tr.EdgeId}: {tr.DistanceM:F0} m / {tr.DurationS:F0} s")
            .Take(12)
            .ToList();
        var candidate=new RouteCandidate {
            Distance=r.DistanceM,Duration=r.DurationS,Geometry=geo,Legs=null,Analysis=new RouteAnalysis {
                Restricted=false,Score=0,Violations=[],Debug=new RouteDebug {
                    Summary = $"{r.Traversals.Count} traversals through PostGIS truck graph.",
                    StartSnap = r.StartSnap ?? startSnap,
                    EndSnap = r.EndSnap ?? endSnap,
                    TraversalCount = r.Traversals.Count,
                    Highlights = routeHighlights
                }
            }
        }
        ;
        var maneuverDtos=maneuvers.Build(r.Traversals,edges);
        return new RouteResponse {
            Code="Ok",Routes=[candidate],SelectedRouteIndex=0,IsTruckSafe=true,Violations=[],Summary=new RouteSummary(r.DistanceM,r.DurationS,(request.DepartureAt??DateTimeOffset.UtcNow).AddSeconds(r.DurationS)),Diagnostics=new RouteDiagnostics {
                Engine=r.Engine,UsedFallback=false,ExpandedStates=r.ExpandedStates,GraphVersion=version,FailureReason=r.FailureReason,StartSnap=r.StartSnap ?? startSnap,EndSnap=r.EndSnap ?? endSnap,TraversalCount=r.Traversals.Count,Highlights=routeHighlights
            }
            ,Maneuvers=maneuverDtos.ToList()
        }
        ;
    }
}
