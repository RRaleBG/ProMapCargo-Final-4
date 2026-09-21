using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Routing;
public interface IPostGisRoutingService {
    Task<RouteResponse> CalculateAsync(RouteRequest request,CancellationToken ct);
}
public sealed class PostGisRoutingService(PostGisRoutingRepository repo,EdgeSnapper snapper,PostGisAStarRouter
    router,ManeuverBuilder maneuvers):IPostGisRoutingService
{
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
        foreach(var tr in r.Traversals) {
            if(!byId.TryGetValue(tr.EdgeId,out var edge))continue;
            var line=(NetTopologySuite.Geometries.LineString)edge.Geometry;
            var pts=line.Coordinates.Select(c=>new[] {
                c.X,c.Y
            }
            ).ToList();
            if(!tr.Forward)pts.Reverse();
            if(coordinates.Count>0)pts.RemoveAt(0);
            coordinates.AddRange(pts);
        }
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
        var candidate=new RouteCandidate {
            Distance=r.DistanceM,Duration=r.DurationS,Geometry=geo,Legs=null,Analysis=new RouteAnalysis {
                Restricted=false,Score=0,Violations=[]
            }
        }
        ;
        var maneuverDtos=maneuvers.Build(r.Traversals,edges);
        return new RouteResponse {
            Code="Ok",Routes=[candidate],SelectedRouteIndex=0,IsTruckSafe=true,Violations=[],Summary=new RouteSummary(r.DistanceM,r.DurationS,(request.DepartureAt??DateTimeOffset.UtcNow).AddSeconds(r.DurationS)),Diagnostics=new RouteDiagnostics {
                Engine=r.Engine,UsedFallback=false,ExpandedStates=r.ExpandedStates,GraphVersion=version
            }
            ,Maneuvers=maneuverDtos.ToList()
        }
        ;
    }
}
