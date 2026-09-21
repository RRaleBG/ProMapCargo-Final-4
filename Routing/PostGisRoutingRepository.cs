using Dapper;
using NetTopologySuite.Geometries;
using Npgsql;
using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Routing;


public sealed class PostGisRoutingRepository(NpgsqlDataSource dataSource)
{
    private const string EdgeColumns = "id, way_id, source_node, target_node, direction, highway, length_m, speed_kmh, routable, ST_AsText(geom) AS wkt, access, vehicle, motor_vehicle, hgv, goods, hazmat, maxheight, maxwidth, maxlength, maxweight, maxaxleload, graph_version, country_code";
   
    
    public async Task<long?> GetActiveGraphVersionAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        const string sql = """
            SELECT graph_version
            FROM routing_graph_versions
            WHERE status = 'ready' AND activated_at IS NOT NULL
            ORDER BY activated_at DESC
            LIMIT 1;
            """;

        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(sql, cancellationToken: ct));
    }



    public async Task<IReadOnlyList<RoadEdge>> FindNearestEdgesAsync(double lat, double lon, long version, double radius, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        const string sql = """
        WITH p AS
        (
            SELECT
                ST_SetSRID(
                    ST_MakePoint(@lon, @lat),
                    4326
                )::geography AS geog
        )
        SELECT
            e.id,
            e.way_id,
            e.source_node,
            e.target_node,
            e.direction,
            e.highway,
            e.length_m,
            e.speed_kmh,
            e.routable,
            ST_AsText(e.geom) AS wkt,
            e.access,
            e.vehicle,
            e.motor_vehicle,
            e.hgv,
            e.goods,
            e.hazmat,
            e.maxheight,
            e.maxwidth,
            e.maxlength,
            e.maxweight,
            e.maxaxleload,
            e.graph_version,
            e.country_code
        FROM road_edges e
        CROSS JOIN p
        WHERE e.graph_version = @version
          AND e.routable
          AND ST_DWithin(
                e.geom::geography,
                p.geog,
                @radius
          )
        ORDER BY
            e.geom::geography <-> p.geog
        LIMIT 24;
        """;

        var rows = await connection.QueryAsync<dynamic>(
            new CommandDefinition(
                sql,
                new
                {
                    lat,
                    lon,
                    version,
                    radius
                },
                cancellationToken: ct));

        return rows.Select(Map).ToList();
    }


    public async Task<IReadOnlyList<RoadEdge>> GetEdgesByIdsAsync(IReadOnlyCollection<long> ids, long version, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var sql = $"""
            SELECT {EdgeColumns}
            FROM road_edges
            WHERE graph_version = @version
              AND id = ANY(@ids);
            """;
        var rows = await connection.QueryAsync<dynamic>(
        new CommandDefinition(
        sql,
        new {
            ids = ids.ToArray(), version
        }
        ,
        cancellationToken: ct));
        return rows.Select(Map).ToList();
    }
    
    
    public async Task<IReadOnlyList<(RoadEdge Edge, bool Forward)>> GetOutgoingAsync(long node, long version,  CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var sql = $"""
            SELECT {EdgeColumns}
            FROM road_edges
            WHERE graph_version = @version
              AND routable
              AND (source_node = @node OR target_node = @node);
            """;
        var rows = await connection.QueryAsync<dynamic>(
        new CommandDefinition(sql, new {
            node, version
        }
        , cancellationToken: ct));
        var result = new List<(RoadEdge Edge, bool Forward)>();
        foreach (var row in rows)
        {
            var edge = Map(row);
            if (edge.SourceNode == node && edge.Direction >= 0)
            {
                result.Add((edge, true));
            }
            if (edge.TargetNode == node && edge.Direction <= 0)
            {
                result.Add((edge, false));
            }
        }
        return result;
    }



    private static RoadEdge Map(dynamic row)
    {
        var geometry = (LineString)new NetTopologySuite.IO.WKTReader().Read((string)row.wkt);
        return new RoadEdge(
        (long)row.id,
        (long)row.way_id,
        (long)row.source_node,
        (long)row.target_node,
        (short)row.direction,
        (string?)row.highway,
        (double)row.length_m,
        (double)row.speed_kmh,
        (bool)row.routable,
        geometry,
        (string?)row.access,
        (string?)row.vehicle,
        (string?)row.motor_vehicle,
        (string?)row.hgv,
        (string?)row.goods,
        (string?)row.hazmat,
        row.maxheight is null ? null : (double?)row.maxheight,
        row.maxwidth is null ? null : (double?)row.maxwidth,
        row.maxlength is null ? null : (double?)row.maxlength,
        row.maxweight is null ? null : (double?)row.maxweight,
        row.maxaxleload is null ? null : (double?)row.maxaxleload,
        (long)row.graph_version,
        (string?)row.country_code);
    }
}
