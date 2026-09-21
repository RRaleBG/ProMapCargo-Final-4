using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Routing;

public sealed class EdgeSnapper(PostGisRoutingRepository repo, TruckEdgeEvaluator evaluator)
{
    public async Task<SnapResult?> SnapAsync(GeoPoint p, TruckProfile t, long version, CancellationToken ct)
    {
        foreach (var radius in new[] { 50d, 100d, 250d, 500d, 1000d })
        {
            foreach (var e in await repo.FindNearestEdgesAsync(p.Lat, p.Lon, version, radius, ct))
            {
                if (!evaluator.Allowed(e, t, out _)) continue;
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
                var frac = line.Length <= 0 ? 0 : idx / line.Length;
                return new SnapResult(e.Id, e.SourceNode, e.TargetNode, input, snapped, DistanceMeters(input, snapped), idx, line.Length - idx,
                    line.Length, frac, e.Direction >= 0, e.Direction <= 0);
            }
        }
        return null;
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