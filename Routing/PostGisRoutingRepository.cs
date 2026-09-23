using Dapper;
using NetTopologySuite.Geometries;
using Npgsql;
namespace ProMapCargo.Api.Routing;


public sealed class PostGisRoutingRepository(NpgsqlDataSource dataSource)
{
    private const string EdgeColumns = "id, way_id, source_node, target_node, direction, highway, name, ref, length_m, speed_kmh, routable, ST_AsText(geom) AS wkt, access, vehicle, motor_vehicle, hgv, goods, hazmat, maxheight, maxwidth, maxlength, maxweight, maxaxleload, graph_version, country_code";

    private const string OutgoingEdgeColumns = "id, way_id, source_node, target_node, direction, highway, name, ref, length_m, speed_kmh, routable, ST_AsText(ST_MakeLine(ST_StartPoint(geom), ST_EndPoint(geom))) AS wkt, ST_X(ST_StartPoint(geom)) AS source_x, ST_Y(ST_StartPoint(geom)) AS source_y, ST_X(ST_EndPoint(geom)) AS target_x, ST_Y(ST_EndPoint(geom)) AS target_y, access, vehicle, motor_vehicle, hgv, goods, hazmat, maxheight, maxwidth, maxlength, maxweight, maxaxleload, graph_version, country_code";


    public async Task<bool> CanConnectAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        const string sql = "SELECT 1;";
        var probe = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
        return probe == 1;
    }


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
                ST_SetSRID(ST_MakePoint(@lon, @lat), 4326) AS geom,
                ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography AS geog,
                @radius / 111320.0 AS radius_degrees
        ),
        candidates AS
        (
            SELECT
                e.id,
                e.way_id,
                e.source_node,
                e.target_node,
                e.direction,
                e.highway,
                e.name,
                e.ref,
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
                e.country_code,
                ST_Distance(e.geom::geography, p.geog) AS distance_m
            FROM road_edges e
            CROSS JOIN p
            WHERE e.graph_version = @version
              AND e.routable
              AND e.geom && ST_Expand(p.geom, p.radius_degrees)
            ORDER BY e.geom <-> p.geom
            LIMIT 256
        )
        SELECT
            id,
            way_id,
            source_node,
            target_node,
            direction,
            highway,
            name,
            ref,
            length_m,
            speed_kmh,
            routable,
            wkt,
            access,
            vehicle,
            motor_vehicle,
            hgv,
            goods,
            hazmat,
            maxheight,
            maxwidth,
            maxlength,
            maxweight,
            maxaxleload,
            graph_version,
            country_code
        FROM candidates
        WHERE distance_m <= @radius
        ORDER BY distance_m
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
                commandTimeout: 120,
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
        new
        {
            ids = ids.ToArray(),
            version
        }
        ,
        cancellationToken: ct));
        return rows.Select(Map).ToList();
    }


    public async Task<IReadOnlyList<(RoadEdge Edge, bool Forward)>> GetOutgoingAsync(long node, long version, CancellationToken ct)
    {
        var outgoingByNode = await GetOutgoingBatchAsync([node], version, ct);
        return outgoingByNode.TryGetValue(node, out var outgoing)
            ? outgoing
            : [];
    }


    public async Task<IReadOnlyDictionary<long, IReadOnlyList<(RoadEdge Edge, bool Forward)>>> GetOutgoingBatchAsync(
        IReadOnlyCollection<long> nodes,
        long version,
        CancellationToken ct)
    {
        if (nodes.Count == 0)
        {
            return new Dictionary<long, IReadOnlyList<(RoadEdge Edge, bool Forward)>>();
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var sql = $"""
            SELECT {OutgoingEdgeColumns}
            FROM road_edges
            WHERE graph_version = @version
              AND routable
              AND (source_node = ANY(@nodes) OR target_node = ANY(@nodes));
            """;
        var rows = await connection.QueryAsync<dynamic>(
            new CommandDefinition(
                sql,
                new
                {
                    nodes = nodes.ToArray(),
                    version
                },
                cancellationToken: ct));

        var result = nodes.ToDictionary<long, long, List<(RoadEdge Edge, bool Forward)>>(
            node => node,
            _ => []);

        foreach (var row in rows)
        {
            RoadEdge edge = Map(row);

            if (edge.Direction >= 0 && result.ContainsKey(edge.SourceNode))
            {
                result[edge.SourceNode].Add((edge, true));
            }

            if (edge.Direction <= 0 && result.ContainsKey(edge.TargetNode))
            {
                result[edge.TargetNode].Add((edge, false));
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<(RoadEdge Edge, bool Forward)>)pair.Value);
    }



    private static RoadEdge Map(dynamic row)
    {
        var geometry = (LineString)new NetTopologySuite.IO.WKTReader().Read((string)row.wkt);
        var sourceCoordinate = TryCoordinate(row, "source_x", "source_y") ?? geometry.Coordinates.FirstOrDefault();
        var targetCoordinate = TryCoordinate(row, "target_x", "target_y") ?? geometry.Coordinates.LastOrDefault();
        return new RoadEdge(
        (long)row.id,
        (long)row.way_id,
        (long)row.source_node,
        (long)row.target_node,
        (short)row.direction,
        (string?)row.highway,
        (string?)row.name,
        (string?)row.@ref,
        (double)row.length_m,
        (double)row.speed_kmh,
        (bool)row.routable,
        geometry,
        sourceCoordinate,
        targetCoordinate,
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

    private static Coordinate? TryCoordinate(dynamic row, string xName, string yName)
    {
        var dictionary = row as IDictionary<string, object>;
        if (dictionary is null ||
            !dictionary.TryGetValue(xName, out var xValue) ||
            !dictionary.TryGetValue(yName, out var yValue) ||
            xValue is null ||
            yValue is null)
        {
            return null;
        }

        return new Coordinate((double)xValue, (double)yValue);
    }
}
