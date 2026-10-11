using System.Globalization;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using ProMapCargo.OfflineRouting;

namespace ProMapCargo.OfflineBundleBuilder;

/// <summary>
/// Roads, turn restrictions and places straight from one or more OSM extracts
/// (e.g. Geofabrik country files), without PostGIS. Several countries are merged by
/// OSM ids, so roads crossing a border connect. The road rules are the importer's
/// (Importer/GraphImporter.cs): same highway set, junction splitting, one-way
/// handling and speeds, so a package from PBF matches one from the imported graph.
/// </summary>
public static class OsmPbfSource
{
    private static readonly HashSet<string> Highways = new(StringComparer.OrdinalIgnoreCase)
    {
        "motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link",
        "tertiary", "tertiary_link", "unclassified", "residential", "living_street", "service", "road", "track",
    };

    // Slim packages (large regions): the road network a truck drives between places;
    // streets inside towns are left out (a destination there snaps to the nearest kept road).
    private static readonly HashSet<string> SlimHighways = new(StringComparer.OrdinalIgnoreCase)
    {
        "motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link", "secondary", "secondary_link",
        "tertiary", "tertiary_link", "unclassified",
    };

    public sealed record Result(
        List<TruckRules.RoadRow> Roads,
        List<(string Type, long From, long To, long[] ViaNodes, long[] ViaWays, string? Except)> Restrictions,
        List<PlaceEntry> Places);

    private sealed record WayRecord(long Id, long[] Nodes, Dictionary<string, string> Tags);

    public static Result Load(IReadOnlyList<string> pbfFiles, bool slim)
    {
        var allowed = slim ? SlimHighways : Highways;

        // Pass 1: ways and restriction relations (nodes are skipped, only ids counted).
        var ways = new Dictionary<long, WayRecord>();
        // Slim packages: minor roads are kept aside to connect ferry piers (port access
        // roads are mostly "service") to the main network afterwards.
        var minorWays = slim ? new List<WayRecord>() : null;
        var restrictions = new Dictionary<long, (string Type, long From, long To, long[] ViaNodes, long[] ViaWays, string? Except)>();
        foreach (var file in pbfFiles)
        {
            Console.WriteLine($"  reading ways: {Path.GetFileName(file)}");
            using var stream = File.OpenRead(file);
            var source = new PBFOsmStreamSource(stream);
            foreach (var element in source)
            {
                if (element is Way way && way.Id is { } wayId && way.Nodes is { Length: >= 2 } && !ways.ContainsKey(wayId))
                {
                    var tags = Tags(way.Tags);
                    if (tags.TryGetValue("highway", out var highway) && allowed.Contains(highway))
                    {
                        ways[wayId] = new WayRecord(wayId, way.Nodes, tags);
                    }
                    else if (minorWays is not null && tags.TryGetValue("highway", out var minor) && Highways.Contains(minor))
                    {
                        minorWays.Add(new WayRecord(wayId, way.Nodes, tags));
                    }
                    else if (IsVehicleFerry(tags))
                    {
                        // Car/truck ferries (Tallinn-Helsinki, Croatian islands) connect the network.
                        tags["highway"] = "ferry";
                        ways[wayId] = new WayRecord(wayId, way.Nodes, tags);
                    }
                }
                else if (element is Relation relation && relation.Id is { } relationId && relation.Members is { } members &&
                         !restrictions.ContainsKey(relationId))
                {
                    var tags = Tags(relation.Tags);
                    if (!tags.TryGetValue("type", out var type) || !type.Equals("restriction", StringComparison.OrdinalIgnoreCase) ||
                        !tags.TryGetValue("restriction", out var restriction))
                    {
                        continue;
                    }

                    var from = members.FirstOrDefault(m => m.Role == "from" && m.Type == OsmGeoType.Way);
                    var to = members.FirstOrDefault(m => m.Role == "to" && m.Type == OsmGeoType.Way);
                    if (from is null || to is null)
                    {
                        continue;
                    }

                    var viaNodes = members.Where(m => m.Role == "via" && m.Type == OsmGeoType.Node).Select(m => m.Id).ToArray();
                    var viaWays = members.Where(m => m.Role == "via" && m.Type == OsmGeoType.Way).Select(m => m.Id).ToArray();
                    tags.TryGetValue("except", out var except);
                    restrictions[relationId] = (restriction, from.Id, to.Id, viaNodes, viaWays, except);
                }
            }
        }

        if (minorWays is not null)
        {
            ConnectFerries(ways, minorWays);
            minorWays = null;
        }

        Console.WriteLine($"  {ways.Count:N0} roads (OSM ways), {restrictions.Count:N0} turn restrictions");

        // Nodes needed: every node of a kept way.
        var needed = new HashSet<long>();
        foreach (var way in ways.Values)
        {
            foreach (var id in way.Nodes) needed.Add(id);
        }

        var nodeIds = needed.ToArray();
        Array.Sort(nodeIds);
        needed.Clear();
        var nodeLat = new int[nodeIds.Length];
        var nodeLon = new int[nodeIds.Length];
        var found = new bool[nodeIds.Length];
        var places = new List<PlaceEntry>();
        var seenPlaces = new HashSet<long>();

        // Pass 2: coordinates of those nodes, and place nodes for the search index.
        foreach (var file in pbfFiles)
        {
            Console.WriteLine($"  reading nodes: {Path.GetFileName(file)}");
            using var stream = File.OpenRead(file);
            var source = new PBFOsmStreamSource(stream);
            foreach (var element in source)
            {
                if (element is not Node node || node.Id is not { } id || node.Latitude is not { } lat || node.Longitude is not { } lon)
                {
                    if (element is not Node) break; // nodes come first in a sorted extract
                    continue;
                }

                var index = Array.BinarySearch(nodeIds, id);
                if (index >= 0)
                {
                    nodeLat[index] = (int)Math.Round(lat * 1e6);
                    nodeLon[index] = (int)Math.Round(lon * 1e6);
                    found[index] = true;
                }

                if (node.Tags is { Count: > 0 } && seenPlaces.Add(id) && PlaceFrom(node, lat, lon) is { } place)
                {
                    places.Add(place);
                }
            }
        }

        // Junctions: way ends, nodes shared by ways (or repeated in one) and via nodes.
        var seen = new HashSet<long>();
        var junctions = new HashSet<long>();
        foreach (var way in ways.Values)
        {
            for (var i = 0; i < way.Nodes.Length; i++)
            {
                var id = way.Nodes[i];
                if (i == 0 || i == way.Nodes.Length - 1 || !seen.Add(id)) junctions.Add(id);
            }
        }

        foreach (var restriction in restrictions.Values)
        {
            foreach (var via in restriction.ViaNodes) junctions.Add(via);
        }

        seen.Clear();

        var roads = new List<TruckRules.RoadRow>();
        long edgeId = 0;
        var coordinates = new List<double>();
        foreach (var way in ways.Values)
        {
            var t = way.Tags;
            var direction = Direction(t);
            long sourceNode = 0, lastNode = 0;
            coordinates.Clear();

            void Emit(long target)
            {
                if (coordinates.Count >= 4)
                {
                    var coords = coordinates.ToArray();
                    var length = LengthMeters(coords);
                    var isFerry = V(t, "highway") == "ferry";
                    roads.Add(new TruckRules.RoadRow(
                        ++edgeId, way.Id, sourceNode, target, isFerry ? (short)0 : direction,
                        V(t, "highway"), V(t, "name"), V(t, "ref"),
                        // A vehicle ferry is open to trucks by definition (access tags refer to people).
                        isFerry ? null : V(t, "access"), isFerry ? null : V(t, "vehicle"), isFerry ? null : V(t, "motor_vehicle"),
                        isFerry ? null : V(t, "hgv"), V(t, "goods"), V(t, "hazmat"),
                        Num(t, "maxheight"), Num(t, "maxwidth"), Num(t, "maxlength"), Weight(t, "maxweight"),
                        isFerry ? FerrySpeed(t, length) : EffectiveSpeed(t), length, coords));
                }
            }

            foreach (var id in way.Nodes)
            {
                var index = Array.BinarySearch(nodeIds, id);
                if (index < 0 || !found[index])
                {
                    // Node outside the extract: the stretch ends here (as in the importer).
                    Emit(lastNode);
                    coordinates.Clear();
                    continue;
                }

                var lon = nodeLon[index] / 1e6;
                var lat = nodeLat[index] / 1e6;
                if (coordinates.Count == 0)
                {
                    sourceNode = id;
                    lastNode = id;
                    coordinates.Add(lon);
                    coordinates.Add(lat);
                    continue;
                }

                coordinates.Add(lon);
                coordinates.Add(lat);
                lastNode = id;
                if (junctions.Contains(id))
                {
                    Emit(id);
                    coordinates.Clear();
                    sourceNode = id;
                    coordinates.Add(lon);
                    coordinates.Add(lat);
                }
            }

            Emit(lastNode);
        }

        Console.WriteLine($"  {roads.Count:N0} road segments between junctions, {places.Count:N0} places");
        return new Result(roads, restrictions.Values.ToList(), places);
    }

    /// <summary>
    /// Adds the minor roads that lead from each ferry end to the kept network (breadth
    /// first, a few steps), so a ferry is not an island in a slim package.
    /// </summary>
    private static void ConnectFerries(Dictionary<long, WayRecord> ways, List<WayRecord> minorWays)
    {
        var mainNodes = new HashSet<long>();
        var ferryEnds = new HashSet<long>();
        foreach (var way in ways.Values)
        {
            if (way.Tags.TryGetValue("highway", out var highway) && highway == "ferry")
            {
                ferryEnds.Add(way.Nodes[0]);
                ferryEnds.Add(way.Nodes[^1]);
            }
            else
            {
                foreach (var id in way.Nodes) mainNodes.Add(id);
            }
        }

        var frontier = new HashSet<long>(ferryEnds.Where(id => !mainNodes.Contains(id)));
        var added = 0;
        for (var step = 0; step < 8 && frontier.Count > 0; step++)
        {
            var next = new HashSet<long>();
            foreach (var way in minorWays)
            {
                if (ways.ContainsKey(way.Id) || !way.Nodes.Any(frontier.Contains))
                {
                    continue;
                }

                ways[way.Id] = way;
                added++;
                if (way.Nodes.Any(mainNodes.Contains))
                {
                    continue; // reached the network
                }

                foreach (var id in way.Nodes) next.Add(id);
            }

            frontier = next;
        }

        Console.WriteLine($"  ferries: {ferryEnds.Count / 2:N0} lines, {added:N0} access roads added");
    }

    private static PlaceEntry? PlaceFrom(Node node, double lat, double lon)
    {
        if (!node.Tags.TryGetValue("place", out var placeType) || !node.Tags.TryGetValue("name", out var placeName))
        {
            return null;
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
            return null;
        }

        node.Tags.TryGetValue("population", out var populationText);
        int.TryParse(populationText?.Replace(".", "").Replace(" ", "").Replace(",", ""), out var population);
        var label = kind switch
        {
            PlaceKind.City or PlaceKind.Town => "grad",
            PlaceKind.Village => "selo",
            PlaceKind.Suburb => "naselje",
            _ => "mesto",
        };

        return PlaceEntry.Create(kind.Value, placeName, label, lat, lon, population);
    }

    // ---- Importer rules (Importer/GraphImporter.cs) ----

    private static Dictionary<string, string> Tags(TagsCollectionBase? tags)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tags is null) return result;
        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag.Key)) result[tag.Key] = tag.Value ?? string.Empty;
        }

        return result;
    }

    private static string? V(Dictionary<string, string> t, string k) => t.TryGetValue(k, out var v) ? v : null;

    private static double? Num(Dictionary<string, string> t, string k) => Parse(V(t, k), true);

    private static double? Weight(Dictionary<string, string> t, string k)
    {
        var x = Parse(V(t, k), false);
        if (x is null) return null;
        return V(t, k)!.Contains("kg", StringComparison.OrdinalIgnoreCase) ? x / 1000d : x;
    }

    private static double? Speed(Dictionary<string, string> t, string k)
    {
        var raw = V(t, k);
        var x = Parse(raw, false);
        if (x is null) return null;
        if (raw!.Contains("mph", StringComparison.OrdinalIgnoreCase)) x *= 1.609344;
        return x is >= 5 and <= 140 ? x : null;
    }

    private static double EffectiveSpeed(Dictionary<string, string> t) =>
        Speed(t, "maxspeed:hgv") ?? Speed(t, "maxspeed") ?? DefaultSpeed(V(t, "highway"));

    private static bool IsVehicleFerry(Dictionary<string, string> t) =>
        string.Equals(V(t, "route"), "ferry", StringComparison.OrdinalIgnoreCase) &&
        new[] { "hgv", "motor_vehicle", "motorcar" }.Any(k => string.Equals(V(t, k), "yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>Ferry speed from its duration tag (H:MM or minutes), else 20 km/h.</summary>
    private static double FerrySpeed(Dictionary<string, string> t, double lengthMeters)
    {
        var raw = V(t, "duration");
        double? hours = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m)) hours = h + m / 60.0;
            else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)) hours = minutes / 60.0;
        }

        // Duration includes boarding; keep within plausible ferry speeds.
        return hours is > 0 ? Math.Clamp(lengthMeters / 1000 / hours.Value, 5, 45) : 20;
    }

    private static double? Parse(string? s, bool meters)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace(',', '.');
        var n = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return null;
        if (meters && s.Contains("ft", StringComparison.OrdinalIgnoreCase)) x *= .3048;
        return x;
    }

    private static short Direction(Dictionary<string, string> t)
    {
        switch (V(t, "oneway")?.Trim().ToLowerInvariant())
        {
            case "yes" or "true" or "1": return 1;
            case "-1" or "reverse": return -1;
            case "no" or "false" or "0": return 0;
        }

        var junction = V(t, "junction")?.ToLowerInvariant();
        if (junction is "roundabout" or "circular") return 1;
        var highway = V(t, "highway")?.ToLowerInvariant();
        return highway is "motorway" or "motorway_link" ? (short)1 : (short)0;
    }

    private static double DefaultSpeed(string? hw) => hw switch
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
        _ => 30,
    };

    /// <summary>Geodesic length (as PostGIS ST_Length on geography, within ~0.1%).</summary>
    private static double LengthMeters(double[] c)
    {
        var length = 0.0;
        for (var i = 2; i < c.Length; i += 2)
        {
            var lat1 = c[i - 1] * Math.PI / 180;
            var lat2 = c[i + 1] * Math.PI / 180;
            var dLat = lat2 - lat1;
            var dLon = (c[i] - c[i - 2]) * Math.PI / 180;
            var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            length += 6_371_008.8 * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        }

        return length;
    }
}
