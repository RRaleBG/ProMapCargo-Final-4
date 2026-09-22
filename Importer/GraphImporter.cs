using NetTopologySuite;
using NetTopologySuite.Geometries;
using Npgsql;
using NpgsqlTypes;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using System.Globalization;
using System.Text.Json;

namespace ProMapCargo.OsmImporter;

public sealed class GraphImporter(string connection)
{
    static readonly GeometryFactory Gf = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
    static readonly HashSet<string> Highways = new(StringComparer.OrdinalIgnoreCase)
    {
        "motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link",
        "tertiary", "tertiary_link", "unclassified", "residential", "living_street", "service", "road", "track"
    };

    public async Task ImportAsync(string pbf, long version, CancellationToken ct)
    {
        Console.WriteLine("[IMPORT] Starting graph import...");
        await using var ds = CreateDataSource();
        await using var db = await ds.OpenConnectionAsync(ct);

        Console.WriteLine("[IMPORT] Executing schema SQL...");
        await using (var cmd = new NpgsqlCommand(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "03-routing-graph.sql")), db)) await cmd.ExecuteNonQueryAsync(ct);

        Console.WriteLine("[IMPORT] Cleaning up previous version data...");
        await using (var cleanup = new NpgsqlCommand("DELETE FROM routing_overlay_edges WHERE graph_version=@v; DELETE FROM routing_edge_cells WHERE graph_version=@v; DELETE FROM routing_node_cells WHERE graph_version=@v; DELETE FROM routing_boundaries WHERE graph_version=@v; DELETE FROM routing_cell_adjacency WHERE graph_version=@v; DELETE FROM routing_cells WHERE graph_version=@v; DELETE FROM compiled_turn_restrictions WHERE graph_version=@v; DELETE FROM turn_restrictions WHERE graph_version=@v; DELETE FROM road_edges WHERE graph_version=@v; DELETE FROM osm_way_nodes WHERE graph_version=@v; DELETE FROM osm_ways WHERE graph_version=@v; DELETE FROM osm_nodes WHERE graph_version=@v; DELETE FROM routing_graph_versions WHERE graph_version=@v;", db))
        {
            cleanup.Parameters.AddWithValue("v", version);
            await cleanup.ExecuteNonQueryAsync(ct);
        }

        Console.WriteLine("[IMPORT] Inserting graph version record as 'building'...");
        await using (var cmd = new NpgsqlCommand("INSERT INTO routing_graph_versions(graph_version,status) VALUES(@v,'building')", db))
        {
            cmd.Parameters.AddWithValue("v", version);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        Console.WriteLine("[IMPORT] Reading PBF file...");
        var nodes = new Dictionary<long, Coordinate>();
        var ways = new List<WayRecord>();
        var restrictions = new List<RelationRecord>();
        using (var fs = File.OpenRead(pbf))
        {
            using var src = new PBFOsmStreamSource(fs);
            foreach (var osm in src)
            {
                ct.ThrowIfCancellationRequested();
                if (osm is Node n && n.Id.HasValue && n.Latitude.HasValue && n.Longitude.HasValue)
                    nodes[n.Id.Value] = new Coordinate(n.Longitude.Value, n.Latitude.Value);
                else if (osm is Way w && w.Id.HasValue && w.Nodes is not null)
                {
                    var tags = Tags(w.Tags);
                    if (tags.TryGetValue("highway", out var hw) && Highways.Contains(hw))
                        ways.Add(new WayRecord(w.Id.Value, w.Nodes.Select(nid => (long)nid).ToArray(), tags));
                }
                else if (osm is Relation rel && rel.Id.HasValue)
                {
                    var tags = Tags(rel.Tags);
                    if (tags.TryGetValue("type", out var type) && type.Equals("restriction", StringComparison.OrdinalIgnoreCase))
                        restrictions.Add(new RelationRecord(rel.Id.Value, rel.Members?.ToArray() ?? [], tags));
                }
            }
        }
        Console.WriteLine($"[IMPORT] PBF parsed: {nodes.Count:n0} nodes, {ways.Count:n0} ways, {restrictions.Count:n0} restrictions");

        Console.WriteLine("[IMPORT] Copying nodes to database...");
        await CopyNodes(ds, nodes, version, ct);
        Console.WriteLine("[IMPORT] Nodes copied.");

        Console.WriteLine("[IMPORT] Copying ways and edges to database...");
        await CopyWays(ds, ways, nodes, version, ct);
        Console.WriteLine("[IMPORT] Ways copied.");

        Console.WriteLine("[IMPORT] Copying restrictions to database...");
        await CopyRestrictions(ds, restrictions, version, ct);
        Console.WriteLine("[IMPORT] Restrictions copied.");

        Console.WriteLine("[IMPORT] Finalizing graph status to 'ready'...");
        await using (var cmd = new NpgsqlCommand("UPDATE routing_graph_versions SET status='ready',activated_at=now() WHERE graph_version=@v;", db))
        {
            cmd.Parameters.AddWithValue("v", version);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        Console.WriteLine($"[IMPORT] Graph {version} ready: nodes={nodes.Count:n0}, ways={ways.Count:n0}, restrictions={restrictions.Count:n0}");
    }

    NpgsqlDataSource CreateDataSource()
    {
        var b = new NpgsqlDataSourceBuilder(connection);
        b.UseNetTopologySuite();
        return b.Build();
    }

    static Dictionary<string, string> Tags(TagsCollectionBase? t)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (t is null)
        {
            return result;
        }

        foreach (var tag in t)
        {
            if (string.IsNullOrWhiteSpace(tag.Key))
            {
                continue;
            }

            /*
             * OSM može sadržati isti ključ više puta.
             *
             * Za routing koristimo poslednju vrednost.
             * Time importer više ne puca na duplim tagovima
             * kao što je "fixme".
             */
            result[tag.Key] = tag.Value ?? string.Empty;
        }

        return result;
    }

    async Task CopyNodes(NpgsqlDataSource ds, Dictionary<long, Coordinate> nodes, long v, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        await using var w = c.BeginBinaryImport("COPY osm_nodes(graph_version,id,geom) FROM STDIN (FORMAT BINARY)");
        foreach (var (id, co) in nodes)
        {
            await w.StartRowAsync(ct);
            w.Write(v);
            w.Write(id);
            w.Write(Gf.CreatePoint(co), NpgsqlDbType.Geometry);
        }
        await w.CompleteAsync(ct);
    }


    async Task CopyWays(NpgsqlDataSource ds, List<WayRecord> ways, Dictionary<long, Coordinate> nodes, long v, CancellationToken ct)
    {
        Console.WriteLine("[COPYWAYS] Opening database connections...");
        await using var wayConnection = await ds.OpenConnectionAsync(ct);

        await using var wayNodeConnection = await ds.OpenConnectionAsync(ct);

        await using var edgeConnection = await ds.OpenConnectionAsync(ct);
        Console.WriteLine("[COPYWAYS] Starting binary import writers...");

        await using var wayWriter = wayConnection.BeginBinaryImport(
                "COPY osm_ways(" +
                "graph_version,way_id,highway,name,ref,oneway," +
                "access,vehicle,motor_vehicle,hgv,goods,hazmat," +
                "maxheight,maxwidth,maxlength,maxweight,maxaxleload," +
                "maxspeed,maxspeed_hgv,tags" +
                ") FROM STDIN (FORMAT BINARY)");

        await using var wayNodeWriter =  wayNodeConnection.BeginBinaryImport("COPY osm_way_nodes(" + "graph_version,way_id,seq,node_id" + ") FROM STDIN (FORMAT BINARY)");

        Console.WriteLine($"[COPYWAYS] Processing {ways.Count:n0} ways...");
        int processedWays = 0;

        foreach (var x in ways)
        {
            var t = x.Tags;


            await wayWriter.StartRowAsync(ct);

            wayWriter.Write(v);
            wayWriter.Write(x.Id);

            Write(wayWriter, V(t, "highway"));

            Write( wayWriter, V(t, "name"));

            Write(wayWriter,   V(t, "ref"));

            Write(   wayWriter, V(t, "oneway"));

            Write(   wayWriter,
                V(t, "access"));

            Write( wayWriter,  V(t, "vehicle"));

            Write(  wayWriter, V(t, "motor_vehicle"));

            Write( wayWriter, V(t, "hgv"));

            Write(
                wayWriter,
                V(t, "goods"));

            Write(
                wayWriter,
                V(t, "hazmat"));

            Write(
                wayWriter,
                Num(t, "maxheight"));

            Write(
                wayWriter,
                Num(t, "maxwidth"));

            Write(
                wayWriter,
                Num(t, "maxlength"));

            Write(
                wayWriter,
                Weight(t, "maxweight"));

            Write(
                wayWriter,
                Weight(t, "maxaxleload"));

            Write(
                wayWriter,
                Speed(t, "maxspeed"));

            Write(
                wayWriter,
                Speed(t, "maxspeed:hgv"));

            wayWriter.Write(
                JsonSerializer.Serialize(t),
                NpgsqlDbType.Jsonb);

            // ========================================================
            // osm_way_nodes
            // ========================================================

            for (var i = 0; i < x.Nodes.Length; i++)
            {
                await wayNodeWriter.StartRowAsync(ct);

                wayNodeWriter.Write(v);
                wayNodeWriter.Write(x.Id);
                wayNodeWriter.Write(i);
                wayNodeWriter.Write(x.Nodes[i]);
            }

            // ========================================================
            // road_edges
            // ========================================================

            await WriteEdges(
                edgeConnection,
                x,
                nodes,
                v,
                ct);

            processedWays++;
            if (processedWays % 10000 == 0)
            {
                Console.WriteLine($"[COPYWAYS] Processed {processedWays:n0} / {ways.Count:n0} ways...");
            }
        }

        Console.WriteLine("[COPYWAYS] Completing way writer...");
        await wayWriter.CompleteAsync(ct);
        Console.WriteLine("[COPYWAYS] Completing way node writer...");
        await wayNodeWriter.CompleteAsync(ct);
        Console.WriteLine("[COPYWAYS] All writers completed successfully.");
    }


    async Task WriteEdges(NpgsqlConnection c, WayRecord x, Dictionary<long, Coordinate> nodes, long v,  CancellationToken ct)
    {
        if (x.Nodes.Length < 2)
        {
            return;
        }

        var dir = Direction(x.Tags);

        for (var i = 0; i < x.Nodes.Length - 1; i++)
        {
            if (!nodes.TryGetValue(x.Nodes[i], out var a) ||
                !nodes.TryGetValue(x.Nodes[i + 1], out var b))
            {
                continue;
            }

            var line = Gf.CreateLineString([a, b]);

            const string sql = """
            INSERT INTO road_edges(
                graph_version,
                way_id,
                source_node,
                target_node,
                direction,
                highway,
                name,
                ref,
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
                maxspeed,
                maxspeed_hgv,
                speed_kmh,
                length_m,
                routable,
                tags,
                geom,
                is_connector,
                hierarchy_penalty
            )
            VALUES(
                @v,
                @w,
                @s,
                @t,
                @d,
                @hw,
                @name,
                @ref,
                @access,
                @vehicle,
                @motor_vehicle,
                @hgv,
                @goods,
                @hazmat,
                @mh,
                @mw,
                @ml,
                @mwt,
                @mal,
                @ms,
                @msh,
                @speed,
                ST_Length(@geom::geography),
                true,
                @tags,
                @geom,
                @conn,
                @penalty
            )
            """;

            await using var cmd = new NpgsqlCommand(sql, c);

            cmd.Parameters.AddWithValue("v", v);
            cmd.Parameters.AddWithValue("w", x.Id);
            cmd.Parameters.AddWithValue("s", x.Nodes[i]);
            cmd.Parameters.AddWithValue("t", x.Nodes[i + 1]);
            cmd.Parameters.AddWithValue("d", dir);
            cmd.Parameters.AddWithValue("hw", (object?)V(x.Tags, "highway") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("name", (object?)V(x.Tags, "name") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ref", (object?)V(x.Tags, "ref") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("access", (object?)V(x.Tags, "access") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("vehicle", (object?)V(x.Tags, "vehicle") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("motor_vehicle", (object?)V(x.Tags, "motor_vehicle") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("hgv", (object?)V(x.Tags, "hgv") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("goods", (object?)V(x.Tags, "goods") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("hazmat", (object?)V(x.Tags, "hazmat") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("mh", (object?)Num(x.Tags, "maxheight") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("mw", (object?)Num(x.Tags, "maxwidth") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ml", (object?)Num(x.Tags, "maxlength") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("mwt", (object?)Weight(x.Tags, "maxweight") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("mal", (object?)Weight(x.Tags, "maxaxleload") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ms", (object?)Speed(x.Tags, "maxspeed") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("msh", (object?)Speed(x.Tags, "maxspeed:hgv") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("speed", DefaultSpeed(V(x.Tags, "highway")));
            cmd.Parameters.AddWithValue("penalty", HierarchyPenalty(V(x.Tags, "highway"), V(x.Tags, "access"), V(x.Tags, "hgv"), V(x.Tags, "goods")));
            cmd.Parameters.Add("tags", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(x.Tags);
            cmd.Parameters.AddWithValue("geom", line);
            cmd.Parameters.AddWithValue("conn", V(x.Tags, "highway")?.EndsWith("_link", StringComparison.OrdinalIgnoreCase) == true);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }


    async Task CopyRestrictions(NpgsqlDataSource ds, List<RelationRecord> rs, long v, CancellationToken ct)
    {
        Console.WriteLine($"[COPYRESTRICTIONS] Processing {rs.Count:n0} restrictions...");
        await using var c = await ds.OpenConnectionAsync(ct);
        int processedRestrictions = 0;

        foreach (var r in rs)
        {
            var from = r.Members.FirstOrDefault(m => m.Role == "from");
            var to = r.Members.FirstOrDefault(m => m.Role == "to");
            var viaNodes = r.Members.Where(m => m.Role == "via" && m.Type.ToString().Equals("Node", StringComparison.OrdinalIgnoreCase)).Select(m => (long)m.Id).ToArray();
            var viaWays = r.Members.Where(m => m.Role == "via" && m.Type.ToString().Equals("Way", StringComparison.OrdinalIgnoreCase)).Select(m => (long)m.Id).ToArray();
            if (from is null || to is null) continue;
            await using var cmd = new NpgsqlCommand("INSERT INTO turn_restrictions(graph_version,osm_relation_id,restriction,from_way_id,to_way_id,via_node_ids,via_way_ids,except_values,conditional,tags) VALUES(@v,@id,@r,@f,@t,@vn,@vw,@ex,@c,@tags) ON CONFLICT DO NOTHING", c);
            cmd.Parameters.AddWithValue("v", v);
            cmd.Parameters.AddWithValue("id", r.Id);
            cmd.Parameters.AddWithValue("r", (object?)V(r.Tags, "restriction") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("f", from.Id);
            cmd.Parameters.AddWithValue("t", to.Id);
            cmd.Parameters.AddWithValue("vn", viaNodes);
            cmd.Parameters.AddWithValue("vw", viaWays);
            cmd.Parameters.AddWithValue("ex", (object?)V(r.Tags, "except") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("c", (object?)V(r.Tags, "restriction:conditional") ?? DBNull.Value);
            cmd.Parameters.Add("tags", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(r.Tags);
            await cmd.ExecuteNonQueryAsync(ct);

            processedRestrictions++;
            if (processedRestrictions % 1000 == 0)
            {
                Console.WriteLine($"[COPYRESTRICTIONS] Processed {processedRestrictions:n0} / {rs.Count:n0} restrictions...");
            }
        }
        Console.WriteLine("[COPYRESTRICTIONS] All restrictions completed.");
    }

    static void Write(NpgsqlBinaryImporter w, string? v)
    {
        if (v is null) w.WriteNull();
        else w.Write(v);
    }

    static void Write(NpgsqlBinaryImporter w, double? v)
    {
        if (v is null) w.WriteNull();
        else w.Write(v.Value);
    }

    static string? V(Dictionary<string, string> t, string k) => t.TryGetValue(k, out var v) ? v : null;
    static double? Num(Dictionary<string, string> t, string k) => Parse(V(t, k), true);
    static double? Weight(Dictionary<string, string> t, string k)
    {
        var x = Parse(V(t, k), false);
        if (x is null) return null;
        return V(t, k)!.Contains("kg", StringComparison.OrdinalIgnoreCase) ? x / 1000d : x;
    }
    static double? Speed(Dictionary<string, string> t, string k)
    {
        var x = Parse(V(t, k), false);
        return x;
    }
    static double? Parse(string? s, bool meters)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace(',', '.');
        var n = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return null;
        if (meters && s.Contains("ft", StringComparison.OrdinalIgnoreCase)) x *= .3048;
        return x;
    }
    static short Direction(Dictionary<string, string> t) => V(t, "oneway") switch
    {
        "yes" or "true" or "1" => 1,
        "-1" => -1,
        _ => 0
    };


    static double DefaultSpeed(string? hw) => hw switch
    {
        "motorway" => 110,
        "motorway_link" => 70,
        "trunk" => 90,
        "trunk_link" => 60,
        "primary" => 80,
        "primary_link" => 60,
        "secondary" => 70,
        "secondary_link" => 50,
        "tertiary" => 60,
        "tertiary_link" => 45,
        "unclassified" => 40,
        "road" => 35,
        "track" => 18,
        "residential" => 50,
        "living_street" => 20,
        "service" => 20,
        _ => 30
    };

    static float HierarchyPenalty(string? highway, string? access, string? hgv, string? goods)
    {
        var penalty = highway switch
        {
            "motorway" or "trunk" => 0f,
            "motorway_link" or "trunk_link" => 0.05f,
            "primary" or "primary_link" => 0.12f,
            "secondary" or "secondary_link" => 0.22f,
            "tertiary" or "tertiary_link" => 0.4f,
            "unclassified" => 0.7f,
            "residential" => 1.1f,
            "service" => 1.35f,
            "road" => 1.5f,
            "track" => 2.4f,
            _ => 0.85f
        };

        if (string.Equals(access, "destination", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hgv, "destination", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(goods, "destination", StringComparison.OrdinalIgnoreCase))
        {
            penalty += 1.4f;
        }

        if (string.Equals(access, "delivery", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hgv, "delivery", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(goods, "delivery", StringComparison.OrdinalIgnoreCase))
        {
            penalty += 1.1f;
        }

        return penalty;
    }

    sealed record WayRecord(long Id, long[] Nodes, Dictionary<string, string> Tags);
    sealed record RelationRecord(long Id, OsmSharp.RelationMember[] Members, Dictionary<string, string> Tags);
}