using System.Diagnostics;
using Npgsql;
using OsmSharp;
using OsmSharp.Streams;
using ProMapCargo.OfflineBundleBuilder;
using ProMapCargo.OfflineRouting;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: OfflineBundleBuilder <out-dir> <region.osm.pbf> [name=serbia]");
    return 1;
}

var outDir = Path.GetFullPath(args[0]);
var pbfPath = Path.GetFullPath(args[1]);
var name = args.Length > 2 ? args[2] : "serbia";
var connectionString = Environment.GetEnvironmentVariable("PROMAP_POSTGRES")
    ?? "Host=localhost;Port=5432;Database=promapcargo;Username=promap;Password=promap_dev_change_me;Command Timeout=0";
Directory.CreateDirectory(outDir);
var total = Stopwatch.StartNew();

// ---- 1. Roads from PostGIS (latest graph version) ----
Console.WriteLine("Reading roads from PostGIS…");
var roads = new List<TruckRules.RoadRow>();
long graphVersion;
await using (var connection = new NpgsqlConnection(connectionString))
{
    await connection.OpenAsync();
    await using (var versionCommand = new NpgsqlCommand("select max(graph_version) from road_edges", connection))
    {
        graphVersion = (long)(await versionCommand.ExecuteScalarAsync())!;
    }

    const string sql = """
        select id, source_node, target_node, direction, highway, name, ref, access, vehicle, motor_vehicle,
               hgv, goods, hazmat, maxheight, maxwidth, maxlength, maxweight, speed_kmh, length_m,
               ST_AsBinary(geom)
        from road_edges
        where graph_version = @version and routable
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("version", graphVersion);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roads.Add(new TruckRules.RoadRow(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt16(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetDouble(13),
            reader.IsDBNull(14) ? null : reader.GetDouble(14),
            reader.IsDBNull(15) ? null : reader.GetDouble(15),
            reader.IsDBNull(16) ? null : reader.GetDouble(16),
            reader.GetDouble(17),
            reader.GetDouble(18),
            ParseWkbLineString((byte[])reader.GetValue(19))));
    }
}

Console.WriteLine($"  {roads.Count:N0} roads (graph {graphVersion}) in {total.Elapsed.TotalSeconds:0} s");

var allowed = roads.Where(road => road.Coordinates.Length >= 4 && TruckRules.Allowed(road)).ToList();
Console.WriteLine($"  {allowed.Count:N0} usable by the default truck");

// ---- 2. Largest connected network only (drops private parking lots, islands) ----
var nodeIds = new Dictionary<long, int>();
int NodeIndex(long id)
{
    if (!nodeIds.TryGetValue(id, out var index))
    {
        index = nodeIds.Count;
        nodeIds[id] = index;
    }

    return index;
}

foreach (var road in allowed)
{
    NodeIndex(road.SourceNode);
    NodeIndex(road.TargetNode);
}

var parent = Enumerable.Range(0, nodeIds.Count).ToArray();
int Find(int x)
{
    while (parent[x] != x)
    {
        parent[x] = parent[parent[x]];
        x = parent[x];
    }

    return x;
}

foreach (var road in allowed)
{
    var a = Find(nodeIds[road.SourceNode]);
    var b = Find(nodeIds[road.TargetNode]);
    if (a != b) parent[a] = b;
}

var largest = Enumerable.Range(0, nodeIds.Count).GroupBy(Find).MaxBy(group => group.Count())!.Key;
var kept = allowed.Where(road => Find(nodeIds[road.SourceNode]) == largest).ToList();
Console.WriteLine($"  {kept.Count:N0} roads in the main network");

// Compact node numbering for the kept roads.
var nodeMap = new Dictionary<long, int>();
var nodeLat = new List<int>();
var nodeLon = new List<int>();
int MapNode(long id, double lon, double lat)
{
    if (!nodeMap.TryGetValue(id, out var index))
    {
        index = nodeMap.Count;
        nodeMap[id] = index;
        nodeLat.Add((int)Math.Round(lat * 1e6));
        nodeLon.Add((int)Math.Round(lon * 1e6));
    }

    return index;
}

// ---- 3. Bundle arrays ----
var bundle = new GraphBundle { GraphVersion = graphVersion, BuiltAtUtc = DateTime.UtcNow };
var strings = new List<string>();
var stringIndex = new Dictionary<string, int>();
int Str(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return -1;
    if (!stringIndex.TryGetValue(value, out var index))
    {
        index = strings.Count;
        strings.Add(value);
        stringIndex[value] = index;
    }

    return index;
}

var roadCount = kept.Count;
var roadFrom = new int[roadCount];
var roadTo = new int[roadCount];
var roadDirection = new sbyte[roadCount];
var roadWeight = new int[roadCount];
var roadLength = new float[roadCount];
var roadName = new int[roadCount];
var roadRef = new int[roadCount];
var roadClass = new byte[roadCount];
var roadOffset = new long[roadCount];
var roadPoints = new int[roadCount];
var geometry = new List<byte>(40_000_000);

for (var r = 0; r < roadCount; r++)
{
    var road = kept[r];
    var c = road.Coordinates;
    roadFrom[r] = MapNode(road.SourceNode, c[0], c[1]);
    roadTo[r] = MapNode(road.TargetNode, c[^2], c[^1]);
    roadDirection[r] = (sbyte)Math.Sign(road.Direction);
    var seconds = road.LengthM / (TruckRules.Speed(road) / 3.6);
    roadWeight[r] = Math.Max(1, (int)Math.Round(seconds * 10));
    roadLength[r] = (float)road.LengthM;
    roadName[r] = Str(road.Name);
    roadRef[r] = Str(road.Ref);
    roadClass[r] = TruckRules.ClassCode(road.Highway);
    roadOffset[r] = geometry.Count;
    roadPoints[r] = c.Length / 2;

    int lastLat = 0, lastLon = 0;
    for (var i = 0; i < c.Length; i += 2)
    {
        var lat = (int)Math.Round(c[i + 1] * 1e6);
        var lon = (int)Math.Round(c[i] * 1e6);
        GraphBundle.WriteZigZag(geometry, lat - lastLat);
        GraphBundle.WriteZigZag(geometry, lon - lastLon);
        lastLat = lat;
        lastLon = lon;
    }
}

bundle.NodeLat = [.. nodeLat];
bundle.NodeLon = [.. nodeLon];
bundle.RoadFrom = roadFrom;
bundle.RoadTo = roadTo;
bundle.RoadDirection = roadDirection;
bundle.RoadWeight = roadWeight;
bundle.RoadLength = roadLength;
bundle.RoadName = roadName;
bundle.RoadRef = roadRef;
bundle.RoadClass = roadClass;
bundle.RoadGeometryOffset = roadOffset;
bundle.RoadPointCount = roadPoints;
bundle.Strings = [.. strings];
bundle.Geometry = [.. geometry];
Console.WriteLine($"  {bundle.NodeCount:N0} junctions, geometry {geometry.Count / 1e6:0.0} MB");

// ---- 4. Contraction Hierarchies ----
Console.WriteLine("Contracting…");
var contractor = new Contractor(bundle.NodeCount);
for (var r = 0; r < roadCount; r++)
{
    if (roadDirection[r] >= 0) contractor.AddOriginalArc(roadFrom[r], roadTo[r], roadWeight[r], r * 2);
    if (roadDirection[r] <= 0) contractor.AddOriginalArc(roadTo[r], roadFrom[r], roadWeight[r], r * 2 + 1);
}

contractor.Run();
var rank = contractor.Rank;
var up = new List<(int From, int To, int Weight, int Arc)>();
var down = new List<(int From, int To, int Weight, int Arc)>();
foreach (var arc in contractor.FinalArcs())
{
    if (rank[arc.To] > rank[arc.From]) up.Add(arc);
    else down.Add(arc);
}

(int[] Offsets, int[] Node, int[] Weight, int[] Arc) Csr(List<(int From, int To, int Weight, int Arc)> arcs, bool byFrom)
{
    var n = bundle.NodeCount;
    var offsets = new int[n + 1];
    foreach (var a in arcs) offsets[(byFrom ? a.From : a.To) + 1]++;
    for (var i = 0; i < n; i++) offsets[i + 1] += offsets[i];
    var fill = (int[])offsets.Clone();
    var node = new int[arcs.Count];
    var weight = new int[arcs.Count];
    var arcId = new int[arcs.Count];
    foreach (var a in arcs)
    {
        var slot = fill[byFrom ? a.From : a.To]++;
        node[slot] = byFrom ? a.To : a.From;
        weight[slot] = a.Weight;
        arcId[slot] = a.Arc;
    }

    return (offsets, node, weight, arcId);
}

(bundle.UpOffsets, bundle.UpNode, bundle.UpWeight, bundle.UpArc) = Csr(up, byFrom: true);
(bundle.DownOffsets, bundle.DownNode, bundle.DownWeight, bundle.DownArc) = Csr(down, byFrom: false);
bundle.ArcA = [.. contractor.ArcA];
bundle.ArcB = [.. contractor.ArcB];
Console.WriteLine($"  search graph: {up.Count:N0} up + {down.Count:N0} down arcs, {bundle.ArcA.Length:N0} arcs total");

// ---- 5. Snapping grid (0.01° ≈ 1 km cells) ----
const double cell = 0.01;
var minLat = nodeLat.Min() / 1e6 - cell;
var minLon = nodeLon.Min() / 1e6 - cell;
var rows = (int)Math.Ceiling((nodeLat.Max() / 1e6 + cell - minLat) / cell) + 1;
var cols = (int)Math.Ceiling((nodeLon.Max() / 1e6 + cell - minLon) / cell) + 1;
var cells = new List<int>[rows * cols];
for (var r = 0; r < roadCount; r++)
{
    var c = kept[r].Coordinates;
    double loLat = double.MaxValue, hiLat = double.MinValue, loLon = double.MaxValue, hiLon = double.MinValue;
    for (var i = 0; i < c.Length; i += 2)
    {
        loLon = Math.Min(loLon, c[i]); hiLon = Math.Max(hiLon, c[i]);
        loLat = Math.Min(loLat, c[i + 1]); hiLat = Math.Max(hiLat, c[i + 1]);
    }

    for (var row = (int)((loLat - minLat) / cell); row <= (int)((hiLat - minLat) / cell); row++)
    for (var col = (int)((loLon - minLon) / cell); col <= (int)((hiLon - minLon) / cell); col++)
    {
        (cells[row * cols + col] ??= []).Add(r);
    }
}

bundle.GridMinLat = minLat;
bundle.GridMinLon = minLon;
bundle.GridCellDegrees = cell;
bundle.GridRows = rows;
bundle.GridColumns = cols;
bundle.GridOffsets = new int[rows * cols + 1];
var gridRoads = new List<int>();
for (var i = 0; i < cells.Length; i++)
{
    if (cells[i] is { } list) gridRoads.AddRange(list);
    bundle.GridOffsets[i + 1] = gridRoads.Count;
}

bundle.GridRoads = [.. gridRoads];

var graphPath = Path.Combine(outDir, $"{name}.graphbundle");
await using (var file = File.Create(graphPath))
{
    bundle.Write(file);
}

Console.WriteLine($"Wrote {graphPath} ({new FileInfo(graphPath).Length / 1e6:0.0} MB)");

// ---- 6. Place index ----
Console.WriteLine("Reading places from OSM…");
var places = new List<PlaceEntry>();
using (var pbf = File.OpenRead(pbfPath))
{
    var source = new PBFOsmStreamSource(pbf);
    foreach (var element in source)
    {
        if (element is not Node node || node.Tags is null || node.Latitude is null || node.Longitude is null)
        {
            continue;
        }

        if (!node.Tags.TryGetValue("place", out var placeType) || !node.Tags.TryGetValue("name", out var placeName))
        {
            continue;
        }

        PlaceKind? kind = placeType switch
        {
            "city" => PlaceKind.City,
            "town" => PlaceKind.Town,
            "village" => PlaceKind.Village,
            "suburb" or "quarter" or "neighbourhood" => PlaceKind.Suburb,
            "hamlet" or "locality" or "isolated_dwelling" => PlaceKind.Hamlet,
            _ => null,
        };

        if (kind is null)
        {
            continue;
        }

        node.Tags.TryGetValue("population", out var populationText);
        int.TryParse(populationText?.Replace(".", "").Replace(" ", ""), out var population);
        var label = kind switch
        {
            PlaceKind.City => "grad",
            PlaceKind.Town => "grad",
            PlaceKind.Village => "selo",
            PlaceKind.Suburb => "naselje",
            _ => "mesto",
        };

        places.Add(PlaceEntry.Create(kind.Value, placeName, label, node.Latitude.Value, node.Longitude.Value, population));
    }
}

Console.WriteLine($"  {places.Count:N0} places");

// Streets: one entry per (street name, nearest settlement), at the middle of its longest segment.
var settlements = places.Where(p => p.Kind is PlaceKind.City or PlaceKind.Town or PlaceKind.Village or PlaceKind.Suburb).ToList();
var settlementGrid = settlements
    .GroupBy(p => ((int)Math.Floor(p.Lat * 10), (int)Math.Floor(p.Lon * 10)))
    .ToDictionary(g => g.Key, g => g.ToList());

PlaceEntry? NearestSettlement(double lat, double lon)
{
    PlaceEntry? best = null;
    var bestDistance = double.MaxValue;
    var (row, col) = ((int)Math.Floor(lat * 10), (int)Math.Floor(lon * 10));
    for (var dr = -1; dr <= 1; dr++)
    for (var dc = -1; dc <= 1; dc++)
    {
        if (!settlementGrid.TryGetValue((row + dr, col + dc), out var list)) continue;
        foreach (var p in list)
        {
            var d = Math.Pow((p.Lat - lat) * 111, 2) + Math.Pow((p.Lon - lon) * 78, 2);
            // A suburb only counts when it is really close (inside a city).
            if (p.Kind == PlaceKind.Suburb) d *= 2.5;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }
    }

    return best;
}

var streets = new Dictionary<(string Key, string Context), (string Name, double Lat, double Lon, double Length)>();
foreach (var road in kept)
{
    if (string.IsNullOrWhiteSpace(road.Name)) continue;
    var c = road.Coordinates;
    var mid = (c.Length / 4) * 2;
    var (lon, lat) = (c[mid], c[mid + 1]);
    var context = NearestSettlement(lat, lon)?.Name ?? string.Empty;
    var key = (TextNormalizer.Key(road.Name), context);
    if (!streets.TryGetValue(key, out var existing) || road.LengthM > existing.Length)
    {
        streets[key] = (road.Name, lat, lon, road.LengthM);
    }
}

foreach (var ((_, context), street) in streets)
{
    places.Add(PlaceEntry.Create(PlaceKind.Street, street.Name, context, street.Lat, street.Lon, (int)street.Length));
}

var placesPath = Path.Combine(outDir, $"{name}.places");
await using (var file = File.Create(placesPath))
{
    new PlaceIndex(places).Write(file);
}

Console.WriteLine($"Wrote {placesPath}: {places.Count:N0} entries ({new FileInfo(placesPath).Length / 1e6:0.0} MB)");

// ---- 7. Self-test: read back and route ----
Console.WriteLine("Self-test…");
GraphBundle loaded;
await using (var file = File.OpenRead(graphPath))
{
    loaded = GraphBundle.Read(file);
}

var router = new OfflineRouter(loaded);
(string Name, double A, double B, double C, double D)[] tests =
[
    ("Beograd → Novi Sad", 44.8125, 20.4573, 45.2551, 19.8451),
    ("Beograd → Niš", 44.8125, 20.4573, 43.3209, 21.8958),
    ("Subotica → Vranje", 46.1003, 19.6658, 42.5514, 21.9002),
    ("Beograd (grad)", 44.8176, 20.4633, 44.7866, 20.4489),
];
foreach (var test in tests)
{
    var watch = Stopwatch.StartNew();
    var route = router.Route(test.A, test.B, test.C, test.D);
    watch.Stop();
    Console.WriteLine(route is null
        ? $"  {test.Name}: NO ROUTE"
        : $"  {test.Name}: {route.DistanceMeters / 1000:0.0} km, {route.DurationSeconds / 60:0} min, {route.Maneuvers.Count} manevara, {route.SettledNodes} čvorova, {watch.ElapsedMilliseconds} ms");
}

var index = PlaceIndex.Read(File.OpenRead(placesPath));
foreach (var query in new[] { "novi sad", "Нови Сад", "cacak", "knez mihailova", "bulevar oslobodjenja, novi sad" })
{
    var hits = index.Search(query, 3);
    Console.WriteLine($"  \"{query}\": {string.Join(" | ", hits.Select(h => $"{h.Entry.Name} ({h.Entry.Context})"))}");
}

Console.WriteLine($"Total {total.Elapsed.TotalMinutes:0.0} min");
return 0;

static double[] ParseWkbLineString(byte[] wkb)
{
    var little = wkb[0] == 1;
    int ReadInt(int offset) => little ? BitConverter.ToInt32(wkb, offset) : System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(wkb.AsSpan(offset));
    double ReadDouble(int offset) => little ? BitConverter.ToDouble(wkb, offset) : BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(wkb.AsSpan(offset)));
    var count = ReadInt(5);
    var result = new double[count * 2];
    for (var i = 0; i < count; i++)
    {
        result[i * 2] = ReadDouble(9 + i * 16);
        result[i * 2 + 1] = ReadDouble(9 + i * 16 + 8);
    }

    return result;
}
