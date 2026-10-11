using System.Diagnostics;
using Npgsql;
using OsmSharp;
using OsmSharp.Streams;
using ProMapCargo.OfflineBundleBuilder;
using ProMapCargo.OfflineRouting;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  OfflineBundleBuilder <out-dir> <region.osm.pbf> [name=serbia] [--force]        (roads from PostGIS)");
    Console.Error.WriteLine("  OfflineBundleBuilder <out-dir> --name <id> --pbf <a.pbf> [--pbf <b.pbf> ...] [--slim] [--title <text>] [--force]");
    return 1;
}

var outDir = Path.GetFullPath(args[0]);
// PBF mode: roads straight from one or more OSM extracts (countries merged), no PostGIS.
var pbfFiles = new List<string>();
string? pbfName = null;
string? title = null;
var slim = args.Contains("--slim");
for (var i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--pbf") pbfFiles.Add(Path.GetFullPath(args[++i]));
    else if (args[i] == "--name") pbfName = args[++i];
    else if (args[i] == "--title") title = args[++i];
}

var pbfMode = pbfFiles.Count > 0;
var pbfPath = pbfMode ? pbfFiles[0] : Path.GetFullPath(args[1]);
var name = pbfMode
    ? pbfName ?? throw new ArgumentException("--name is required with --pbf")
    : args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "serbia";
var connectionString = Environment.GetEnvironmentVariable("PROMAP_POSTGRES")
    ?? "Host=localhost;Port=5432;Database=promapcargo;Username=promap;Password=promap_dev_change_me;Command Timeout=0";
Directory.CreateDirectory(outDir);
var total = Stopwatch.StartNew();

// ---- 1. Roads: OSM extracts (PBF mode) or PostGIS (latest graph version) ----
var roads = new List<TruckRules.RoadRow>();
long graphVersion;
OsmPbfSource.Result? pbfSource = null;
if (pbfMode)
{
    // Version = newest extract date, so an unchanged download needs no rebuild.
    graphVersion = long.Parse(pbfFiles.Max(f => File.GetLastWriteTimeUtc(f))!.ToString("yyyyMMddHHmm")) * 10 + (slim ? 1 : 0);
    var existing = Path.Combine(outDir, $"{name}.graphbundle");
    if (!args.Contains("--force") && File.Exists(existing) && File.Exists(Path.Combine(outDir, $"{name}.places")))
    {
        try
        {
            using var headerStream = File.OpenRead(existing);
            if (GraphBundle.ReadHeader(headerStream).GraphVersion == graphVersion)
            {
                Console.WriteLine($"Package {name} is up to date ({graphVersion}). Nothing to do.");
                return 0;
            }
        }
        catch (InvalidDataException)
        {
            // Older format: rebuild.
        }
    }

    Console.WriteLine($"Reading {pbfFiles.Count} OSM extract(s){(slim ? " (slim: main roads)" : "")}…");
    pbfSource = OsmPbfSource.Load(pbfFiles, slim);
    roads = pbfSource.Roads;
}
else
{
Console.WriteLine("Reading roads from PostGIS…");
await using (var connection = new NpgsqlConnection(connectionString))
{
    await connection.OpenAsync();
    await using (var versionCommand = new NpgsqlCommand("select max(graph_version) from road_edges", connection))
    {
        graphVersion = (long)(await versionCommand.ExecuteScalarAsync())!;
    }

    const string sql = """
        select id, way_id, source_node, target_node, direction, highway, name, ref, access, vehicle, motor_vehicle,
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
        string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
        double? Number(int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
        roads.Add(new TruckRules.RoadRow(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt16(4),
            Text(5), Text(6), Text(7), Text(8), Text(9), Text(10), Text(11), Text(12), Text(13),
            Number(14), Number(15), Number(16), Number(17),
            reader.GetDouble(18),
            reader.GetDouble(19),
            ParseWkbLineString((byte[])reader.GetValue(20))));
    }
}

}

Console.WriteLine($"  {roads.Count:N0} roads (graph {graphVersion}) in {total.Elapsed.TotalSeconds:0} s");

// Nothing to do when the package already matches this graph and format (use --force to rebuild).
var existingGraph = Path.Combine(outDir, $"{name}.graphbundle");
if (!pbfMode && !args.Contains("--force") && File.Exists(existingGraph) && File.Exists(Path.Combine(outDir, $"{name}.places")))
{
    try
    {
        using var headerStream = File.OpenRead(existingGraph);
        if (GraphBundle.ReadHeader(headerStream).GraphVersion == graphVersion)
        {
            Console.WriteLine($"Package {name} is up to date (graph {graphVersion}). Nothing to do.");
            return 0;
        }
    }
    catch (InvalidDataException)
    {
        // Older package format: rebuild.
    }
}

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

// Every sizeable network is kept, not only the largest: a region can consist of parts
// joined only by sea (e.g. Finland), and ferries may be missing from an extract.
var componentSizes = Enumerable.Range(0, nodeIds.Count).GroupBy(Find).ToDictionary(g => g.Key, g => g.Count());
var largestSize = componentSizes.Values.Max();
var keptComponents = componentSizes.Where(c => c.Value == largestSize || c.Value >= 20_000).Select(c => c.Key).ToHashSet();
Console.WriteLine($"  networks kept: {keptComponents.Count} ({string.Join(", ", componentSizes.Where(c => keptComponents.Contains(c.Key)).Select(c => c.Value.ToString("N0")))} junctions)");
var kept = allowed.Where(road => keptComponents.Contains(Find(nodeIds[road.SourceNode]))).ToList();
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

// ---- 3b. Turn restrictions: one node per arriving road at restricted junctions ----
// Same rules as the server (Routing/TurnRestrictionMatcher.cs): via-node rules only,
// via-way rules and rules that exempt trucks (except=hgv) are skipped.
var rulesByNode = new Dictionary<long, List<(string Type, long From, long To)>>();
void AddRule(string type, long from, long to, long[] viaNodes, long[] viaWays, string? except)
{
    if (viaNodes.Length == 0 || viaWays.Length > 0) return;
    if (except?.Split([';', ','], StringSplitOptions.TrimEntries).Any(v => v.Equals("hgv", StringComparison.OrdinalIgnoreCase)) == true) return;
    if (!type.StartsWith("no_", StringComparison.OrdinalIgnoreCase) && !type.StartsWith("only_", StringComparison.OrdinalIgnoreCase)) return;
    foreach (var via in viaNodes)
    {
        (rulesByNode.TryGetValue(via, out var list) ? list : rulesByNode[via] = []).Add((type, from, to));
    }
}

if (pbfSource is not null)
{
    foreach (var r in pbfSource.Restrictions) AddRule(r.Type, r.From, r.To, r.ViaNodes, r.ViaWays, r.Except);
}
else
await using (var connection = new NpgsqlConnection(connectionString))
{
    await connection.OpenAsync();
    const string restrictionSql = """
        select restriction, from_way_id, to_way_id, via_node_ids, via_way_ids, except_values
        from turn_restrictions where graph_version = @version
        """;
    await using var command = new NpgsqlCommand(restrictionSql, connection);
    command.Parameters.AddWithValue("version", graphVersion);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var type = reader.IsDBNull(0) ? null : reader.GetString(0);
        if (type is null || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3)) continue;
        var viaNodes = (long[])reader.GetValue(3);
        var viaWays = reader.IsDBNull(4) ? [] : (long[])reader.GetValue(4);
        var except = reader.IsDBNull(5) ? null : reader.GetString(5);
        AddRule(type, reader.GetInt64(1), reader.GetInt64(2), viaNodes, viaWays, except);
    }
}

var osmNodeOf = new long[nodeMap.Count];
foreach (var (osmId, nodeIndex) in nodeMap) osmNodeOf[nodeIndex] = osmId;
var baseNodeCount = nodeMap.Count;
var restricted = new HashSet<int>();
for (var n = 0; n < baseNodeCount; n++)
{
    if (rulesByNode.ContainsKey(osmNodeOf[n])) restricted.Add(n);
}

bool TurnAllowed(long fromWay, long toWay, int node)
{
    if (!rulesByNode.TryGetValue(osmNodeOf[node], out var rules)) return true;
    foreach (var (type, from, to) in rules)
    {
        if (from != fromWay) continue;
        if (type.StartsWith("no_", StringComparison.OrdinalIgnoreCase) && to == toWay) return false;
        if (type.StartsWith("only_", StringComparison.OrdinalIgnoreCase) && to != toWay) return false;
    }

    return true;
}

var arcCodes = roadCount * 2;
bool ArcExists(int code) => code % 2 == 0 ? roadDirection[code / 2] >= 0 : roadDirection[code / 2] <= 0;
int ArcFromNode(int code) => code % 2 == 0 ? roadFrom[code / 2] : roadTo[code / 2];
int ArcToNode(int code) => code % 2 == 0 ? roadTo[code / 2] : roadFrom[code / 2];

var arriveNode = new int[arcCodes];
Array.Fill(arriveNode, -1);
var copiesAt = new Dictionary<int, List<(int ArcIn, int Copy)>>();
for (var code = 0; code < arcCodes; code++)
{
    if (!ArcExists(code)) continue;
    var to = ArcToNode(code);
    if (!restricted.Contains(to))
    {
        arriveNode[code] = to;
        continue;
    }

    var copy = nodeLat.Count;
    nodeLat.Add(nodeLat[to]);
    nodeLon.Add(nodeLon[to]);
    arriveNode[code] = copy;
    (copiesAt.TryGetValue(to, out var list) ? list : copiesAt[to] = []).Add((code, copy));
}

var departOffsets = new int[arcCodes + 1];
var departNodes = new List<int>(arcCodes);
var forbiddenTurns = 0;
for (var code = 0; code < arcCodes; code++)
{
    if (ArcExists(code))
    {
        var from = ArcFromNode(code);
        if (copiesAt.TryGetValue(from, out var copies))
        {
            var outWay = kept[code / 2].WayId;
            foreach (var (arcIn, copy) in copies)
            {
                if (TurnAllowed(kept[arcIn / 2].WayId, outWay, from)) departNodes.Add(copy);
                else forbiddenTurns++;
            }
        }
        else
        {
            departNodes.Add(from);
        }
    }

    departOffsets[code + 1] = departNodes.Count;
}

Console.WriteLine($"  turn restrictions: {restricted.Count:N0} junctions split into {nodeLat.Count - baseNodeCount:N0} nodes, {forbiddenTurns:N0} turns forbidden");

// Posted limits (sparse) to check a route against a larger truck on the phone.
var limitRoads = new List<int>();
var limitHeight = new List<float>();
var limitWidth = new List<float>();
var limitLength = new List<float>();
var limitWeight = new List<float>();
for (var r = 0; r < roadCount; r++)
{
    var road = kept[r];
    if (road.MaxHeight is null && road.MaxWidth is null && road.MaxLength is null && road.MaxWeight is null) continue;
    limitRoads.Add(r);
    limitHeight.Add((float)(road.MaxHeight ?? 0));
    limitWidth.Add((float)(road.MaxWidth ?? 0));
    limitLength.Add((float)(road.MaxLength ?? 0));
    limitWeight.Add((float)(road.MaxWeight ?? 0));
}

bundle.ArriveNode = arriveNode;
bundle.DepartOffsets = departOffsets;
bundle.DepartNodes = [.. departNodes];
bundle.LimitRoad = [.. limitRoads];
bundle.LimitHeight = [.. limitHeight];
bundle.LimitWidth = [.. limitWidth];
bundle.LimitLength = [.. limitLength];
bundle.LimitWeight = [.. limitWeight];

bundle.NodeLat = [.. nodeLat];
bundle.NodeLon = [.. nodeLon];
bundle.Name = title ?? name;
bundle.MinLat = nodeLat.Min() / 1e6;
bundle.MaxLat = nodeLat.Max() / 1e6;
bundle.MinLon = nodeLon.Min() / 1e6;
bundle.MaxLon = nodeLon.Max() / 1e6;
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
Console.WriteLine($"  {bundle.NodeCount:N0} nodes, geometry {geometry.Count / 1e6:0.0} MB, {limitRoads.Count:N0} roads with posted limits");

// ---- 4. Contraction Hierarchies ----
Console.WriteLine("Contracting…");
var contractor = new Contractor(bundle.NodeCount);
for (var code = 0; code < arcCodes; code++)
{
    if (!ArcExists(code)) continue;
    for (var i = departOffsets[code]; i < departOffsets[code + 1]; i++)
    {
        contractor.AddOriginalArc(departNodes[i], arriveNode[code], roadWeight[code / 2], code);
    }
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
if (pbfSource is not null)
{
    places.AddRange(pbfSource.Places);
}
else
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
    ("Beograd Slavija → Autokomanda", 44.8023, 20.4664, 44.7889, 20.4746),
    ("Novi Sad centar → Liman", 45.2551, 19.8451, 45.2418, 19.8462),
    ("Niš centar → Medijana", 43.3209, 21.8958, 43.3125, 21.9200),
    ("Beograd → Zagreb", 44.8125, 20.4573, 45.8150, 15.9819),
    ("Sarajevo → Podgorica", 43.8563, 18.4131, 42.4304, 19.2594),
    ("Skoplje → Tirana", 41.9981, 21.4254, 41.3275, 19.8187),
    ("Ljubljana → Priština", 46.0569, 14.5058, 42.6629, 21.1655),
    ("Budimpešta → Beč", 47.4979, 19.0402, 48.2082, 16.3738),
    ("Bratislava → Prag", 48.1486, 17.1077, 50.0755, 14.4378),
    ("Beograd → Minhen", 44.8125, 20.4573, 48.1351, 11.5820),
    ("Varšava → Talin", 52.2297, 21.0122, 59.4370, 24.7536),
    ("Riga → Helsinki (trajekt Talin)", 56.9496, 24.1052, 60.1699, 24.9384),
];
foreach (var test in tests.Where(t => loaded.Contains(t.A, t.B) && loaded.Contains(t.C, t.D)))
{
    var watch = Stopwatch.StartNew();
    var route = router.Route(test.A, test.B, test.C, test.D, new TruckDimensions(4.2, 2.6, 18.75, 44));
    watch.Stop();
    Console.WriteLine(route is null
        ? $"  {test.Name}: NO ROUTE"
        : $"  {test.Name}: {route.DistanceMeters / 1000:0.0} km, {route.DurationSeconds / 60:0} min, {route.Maneuvers.Count} manevara, {route.SettledNodes} čvorova, {watch.ElapsedMilliseconds} ms, ograničenja za 44 t / 4,2 m: {route.Violations.Count}");
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
