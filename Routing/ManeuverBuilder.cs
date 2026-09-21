using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Routing;
public sealed class ManeuverBuilder
{
    public IReadOnlyList<RouteManeuverDto> Build(IReadOnlyList<RoutedTraversal> traversals,IReadOnlyList<RoadEdge> edges) {
        var map=edges.ToDictionary(x=>x.Id);
        var result=new List<RouteManeuverDto>();
        double cumulative=0;
        for(var i=0;
        i<traversals.Count;
        i++) {
            var t=traversals[i];
            if(!map.TryGetValue(t.EdgeId,out var e))continue;
            var c=e.Geometry.Coordinates.FirstOrDefault();
            var type=i==0?"Depart":i==traversals.Count-1?"Arrive":"Continue";
            if(i>0&&map.TryGetValue(traversals[i-1].EdgeId,out var p)) {
                var a=Heading(p.Geometry.Coordinates[^2],p.Geometry.Coordinates[^1],traversals[i-1].Forward);
                var b=Heading(e.Geometry.Coordinates[^2],e.Geometry.Coordinates[^1],t.Forward);
                var delta=((b-a+540)%360)-180;
                if(delta>25)type="Right";
                else if(delta<-25)type="Left";
            }
            result.Add(new RouteManeuverDto(i,type,type=="Depart"?"Krenite":type=="Arrive"?"Stigli ste":$"Nastavite {e.Highway}",t.DistanceM,cumulative,c?.Y??0,c?.X??0,null,null,null));
            cumulative+=t.DistanceM;
        }
        return result;
    }
    static double Heading(NetTopologySuite.Geometries.Coordinate a,NetTopologySuite.Geometries.Coordinate b,bool forward) {
        if(!forward)(a,b)=(b,a);
        var d=(b.X-a.X)*Math.PI/180;
        var p1=a.Y*Math.PI/180;
        var p2=b.Y*Math.PI/180;
        return (Math.Atan2(Math.Sin(d)*Math.Cos(p2),Math.Cos(p1)*Math.Sin(p2)-Math.Sin(p1)*Math.Cos(p2)*Math.Cos(d))*180/Math.PI+360)%360;
    }
}
