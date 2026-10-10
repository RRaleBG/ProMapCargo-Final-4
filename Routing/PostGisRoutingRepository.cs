using Dapper;
using NetTopologySuite.Geometries;
using Npgsql;

namespace ProMapCargo.Api.Routing;

public sealed class PostGisRoutingRepository(
    NpgsqlDataSource dataSource,
    ILogger<PostGisRoutingRepository> logger)
{
    private const string EdgeColumns = "id, way_id, source_node, target_node, direction, highway, name, ref, length_m, speed_kmh, routable, ST_AsText(geom) AS wkt, access, vehicle, motor_vehicle, hgv, goods, hazmat, maxheight, maxwidth, maxlength, maxweight, maxaxleload, graph_version, country_code";

    /*
     * Lightweight column set used by the A* search. Column order is
     * significant: ReadLightEdge reads by ordinal. No WKT is produced or
     * parsed; only the end point coordinates are returned. Name/ref are
     * not needed during the search (terminal edges and the final route
     * are loaded with EdgeColumns).
     */
    private const string LightEdgeColumns = "id, way_id, source_node, target_node, direction, highway, length_m, speed_kmh, ST_X(ST_StartPoint(geom)) AS source_x, ST_Y(ST_StartPoint(geom)) AS source_y, ST_X(ST_EndPoint(geom)) AS target_x, ST_Y(ST_EndPoint(geom)) AS target_y, access, vehicle, motor_vehicle, hgv, goods, hazmat, maxheight, maxwidth, maxlength, maxweight, maxaxleload, country_code";

    /*
     * Shared placeholder for edges loaded by the search. The A* router
     * only reads SourceCoordinate/TargetCoordinate from these edges.
     */
    private static readonly LineString PlaceholderGeometry =
        new GeometryFactory().CreateLineString(Array.Empty<Coordinate>());


    public async Task<bool> CanConnectAsync(CancellationToken ct)
    {
        logger.LogInformation("PostGIS connectivity check started.");

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            const string sql = "SELECT 1;";
            var probe = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
            var connected = probe == 1;

            logger.LogInformation("PostGIS connectivity check completed. Connected={Connected}", connected);
            return connected;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostGIS connectivity check failed.");
            throw;
        }
    }


    public async Task<long?> GetActiveGraphVersionAsync(CancellationToken ct)
    {
        logger.LogInformation("PostGIS query started: active graph version lookup.");

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);

            const string sql = """
                SELECT graph_version
                FROM routing_graph_versions
                WHERE status = 'ready' AND activated_at IS NOT NULL
                ORDER BY activated_at DESC
                LIMIT 1;
                """;

            var graphVersion = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(sql, cancellationToken: ct));
            logger.LogInformation("PostGIS query completed: active graph version lookup. GraphVersion={GraphVersion}", graphVersion);
            return graphVersion;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostGIS query failed: active graph version lookup.");
            throw;
        }
    }



    public async Task<IReadOnlyList<RoadEdge>> FindNearestEdgesAsync(double lat, double lon, long version, double radius, CancellationToken ct)
    {
        logger.LogInformation(
            "PostGIS query started: nearest edges. Lat={Lat} Lon={Lon} GraphVersion={GraphVersion} RadiusMeters={RadiusMeters}",
            lat,
            lon,
            version,
            radius);

        try
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

                var edges = rows.Select(Map).ToList();
                logger.LogInformation(
                    "PostGIS query completed: nearest edges. GraphVersion={GraphVersion} ReturnedEdges={ReturnedEdges}",
                    version,
                    edges.Count);
                return edges;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "PostGIS query failed: nearest edges. Lat={Lat} Lon={Lon} GraphVersion={GraphVersion} RadiusMeters={RadiusMeters}",
                    lat,
                    lon,
                    version,
                    radius);
                throw;
            }
        }


    public async Task<IReadOnlyList<RoadEdge>> GetEdgesByIdsAsync(IReadOnlyCollection<long> ids, long version, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            logger.LogInformation("PostGIS query skipped: get edges by ids called with empty id set. GraphVersion={GraphVersion}", version);
            return [];
        }

        logger.LogInformation("PostGIS query started: get edges by ids. GraphVersion={GraphVersion} RequestedIds={RequestedIds}", version, ids.Count);

        try
        {
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
                    },
                    cancellationToken: ct));

            var edges = rows.Select(Map).ToList();
            logger.LogInformation("PostGIS query completed: get edges by ids. GraphVersion={GraphVersion} ReturnedEdges={ReturnedEdges}", version, edges.Count);
            return edges;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostGIS query failed: get edges by ids. GraphVersion={GraphVersion} RequestedIds={RequestedIds}", version, ids.Count);
            throw;
        }
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
            logger.LogInformation("PostGIS query skipped: outgoing batch called with empty node set. GraphVersion={GraphVersion}", version);
            return new Dictionary<long, IReadOnlyList<(RoadEdge Edge, bool Forward)>>();
        }

        logger.LogInformation("PostGIS query started: outgoing batch. GraphVersion={GraphVersion} RequestedNodes={RequestedNodes}", version, nodes.Count);

        try
        {
            var sql = $"""
                SELECT {LightEdgeColumns}
                FROM road_edges
                WHERE graph_version = @version
                  AND routable
                  AND (source_node = ANY(@nodes) OR target_node = ANY(@nodes));
                """;

            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("nodes", nodes.ToArray());

            var result = nodes.Distinct().ToDictionary<long, long, List<(RoadEdge Edge, bool Forward)>>(
                node => node,
                _ => []);

            var pool = new Dictionary<string, string>();

            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var edge = ReadLightEdge(reader, version, pool);

                    if (edge.Direction >= 0 && result.TryGetValue(edge.SourceNode, out var forwardList))
                    {
                        forwardList.Add((edge, true));
                    }

                    if (edge.Direction <= 0 && result.TryGetValue(edge.TargetNode, out var reverseList))
                    {
                        reverseList.Add((edge, false));
                    }
                }
            }

            var finalResult = result.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<(RoadEdge Edge, bool Forward)>)pair.Value);

            var traversableOptions = finalResult.Sum(pair => pair.Value.Count);
            logger.LogInformation(
                "PostGIS query completed: outgoing batch. GraphVersion={GraphVersion} RequestedNodes={RequestedNodes} TraversableOptions={TraversableOptions}",
                version,
                nodes.Count,
                traversableOptions);

            return finalResult;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "PostGIS query failed: outgoing batch. GraphVersion={GraphVersion} RequestedNodes={RequestedNodes}",
                version,
                nodes.Count);
            throw;
        }
    }


    /// <summary>
    /// Loads the routable graph inside a bounding box with ONE query and
    /// returns the traversable options per node. Only nodes whose own
    /// coordinate lies inside the box are returned: every edge incident to
    /// such a node intersects the box, so their option lists are complete.
    /// Nodes outside the box must still be loaded lazily.
    /// </summary>
    public async Task<Dictionary<long, IReadOnlyList<(RoadEdge Edge, bool Forward)>>> LoadAreaAdjacencyAsync(
        double minLon,
        double minLat,
        double maxLon,
        double maxLat,
        long version,
        CancellationToken ct)
    {
        logger.LogInformation(
            "PostGIS query started: area adjacency preload. GraphVersion={GraphVersion} Box={MinLon},{MinLat},{MaxLon},{MaxLat}",
            version,
            minLon,
            minLat,
            maxLon,
            maxLat);

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var sql = $"""
                SELECT {LightEdgeColumns}
                FROM road_edges
                WHERE graph_version = @version
                  AND routable
                  AND geom && ST_MakeEnvelope(@minLon, @minLat, @maxLon, @maxLat, 4326);
                """;

            await using var command = dataSource.CreateCommand(sql);
            command.CommandTimeout = 120;
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("minLon", minLon);
            command.Parameters.AddWithValue("minLat", minLat);
            command.Parameters.AddWithValue("maxLon", maxLon);
            command.Parameters.AddWithValue("maxLat", maxLat);

            var lists = new Dictionary<long, List<(RoadEdge Edge, bool Forward)>>();
            var pool = new Dictionary<string, string>();
            var edgeCount = 0;

            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var edge = ReadLightEdge(reader, version, pool);
                    edgeCount++;

                    if (edge.Direction >= 0 &&
                        edge.SourceCoordinate is { } source &&
                        Inside(source, minLon, minLat, maxLon, maxLat))
                    {
                        AddOption(lists, edge.SourceNode, edge, true);
                    }

                    if (edge.Direction <= 0 &&
                        edge.TargetCoordinate is { } target &&
                        Inside(target, minLon, minLat, maxLon, maxLat))
                    {
                        AddOption(lists, edge.TargetNode, edge, false);
                    }
                }
            }

            var result = new Dictionary<long, IReadOnlyList<(RoadEdge Edge, bool Forward)>>(lists.Count);

            foreach (var pair in lists)
            {
                result[pair.Key] = pair.Value;
            }

            logger.LogInformation(
                "PostGIS query completed: area adjacency preload. GraphVersion={GraphVersion} Edges={Edges} Nodes={Nodes} ElapsedMs={ElapsedMs}",
                version,
                edgeCount,
                result.Count,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PostGIS query failed: area adjacency preload. GraphVersion={GraphVersion}", version);
            throw;
        }
    }

    private static bool Inside(Coordinate c, double minLon, double minLat, double maxLon, double maxLat) =>
        c.X >= minLon && c.X <= maxLon && c.Y >= minLat && c.Y <= maxLat;

    private static void AddOption(
        Dictionary<long, List<(RoadEdge Edge, bool Forward)>> lists,
        long node,
        RoadEdge edge,
        bool forward)
    {
        if (!lists.TryGetValue(node, out var list))
        {
            list = new List<(RoadEdge Edge, bool Forward)>(3);
            lists[node] = list;
        }

        list.Add((edge, forward));
    }

    private static RoadEdge ReadLightEdge(
        Npgsql.NpgsqlDataReader reader,
        long version,
        Dictionary<string, string> pool) =>
        new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt16(4),
            ReadPooled(reader, 5, pool),
            null,
            null,
            reader.GetDouble(6),
            reader.GetDouble(7),
            true,
            PlaceholderGeometry,
            new Coordinate(reader.GetDouble(8), reader.GetDouble(9)),
            new Coordinate(reader.GetDouble(10), reader.GetDouble(11)),
            ReadPooled(reader, 12, pool),
            ReadPooled(reader, 13, pool),
            ReadPooled(reader, 14, pool),
            ReadPooled(reader, 15, pool),
            ReadPooled(reader, 16, pool),
            ReadPooled(reader, 17, pool),
            ReadNullableDouble(reader, 18),
            ReadNullableDouble(reader, 19),
            ReadNullableDouble(reader, 20),
            ReadNullableDouble(reader, 21),
            ReadNullableDouble(reader, 22),
            version,
            ReadPooled(reader, 23, pool));

    private static double? ReadNullableDouble(Npgsql.NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    /// <summary>
    /// Reads a text column and reuses one string instance per distinct
    /// value (highway/access/... have only a handful of distinct values),
    /// which keeps a large preloaded graph small.
    /// </summary>
    private static string? ReadPooled(Npgsql.NpgsqlDataReader reader, int ordinal, Dictionary<string, string> pool)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal);

        if (pool.TryGetValue(value, out var existing))
        {
            return existing;
        }

        pool[value] = value;
        return value;
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
