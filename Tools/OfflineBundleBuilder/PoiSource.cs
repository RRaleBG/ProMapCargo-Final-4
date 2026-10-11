using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using ProMapCargo.OfflineRouting;

namespace ProMapCargo.OfflineBundleBuilder;

/// <summary>
/// Points of interest for the search index: fuel stations, truck stops and truck parking,
/// border crossings, companies, factories and warehouses. Mapped as a node or as an area
/// (way); an area is placed at the average of its nodes. Relations are not read.
/// </summary>
public static class PoiSource
{
    public sealed record Poi(PlaceKind Kind, string Name, string Category, double Lat, double Lon, int Importance);

    private sealed record Candidate(PlaceKind Kind, string Name, string Category, int Importance);

    public static List<Poi> Load(IReadOnlyList<string> pbfFiles)
    {
        // Pass 1: ways that are a point of interest, and the nodes they need.
        var wayCandidates = new Dictionary<long, (Candidate Poi, long[] Nodes)>();
        foreach (var file in pbfFiles)
        {
            using var stream = File.OpenRead(file);
            foreach (var element in new PBFOsmStreamSource(stream))
            {
                if (element is Way { Id: { } id, Nodes.Length: >= 2 } way && !wayCandidates.ContainsKey(id) &&
                    Classify(way.Tags) is { } poi)
                {
                    wayCandidates[id] = (poi, way.Nodes);
                }
            }
        }

        var nodeIds = wayCandidates.Values.SelectMany(w => w.Nodes).Distinct().ToArray();
        Array.Sort(nodeIds);
        var lat = new double[nodeIds.Length];
        var lon = new double[nodeIds.Length];
        var found = new bool[nodeIds.Length];

        // Pass 2: node points of interest, and the coordinates of the area nodes.
        var pois = new List<Poi>();
        var seenNodes = new HashSet<long>();
        foreach (var file in pbfFiles)
        {
            using var stream = File.OpenRead(file);
            foreach (var element in new PBFOsmStreamSource(stream))
            {
                if (element is not Node { Id: { } id, Latitude: { } nodeLat, Longitude: { } nodeLon } node)
                {
                    if (element is not Node) break; // nodes come first in a sorted extract
                    continue;
                }

                var index = Array.BinarySearch(nodeIds, id);
                if (index >= 0)
                {
                    lat[index] = nodeLat;
                    lon[index] = nodeLon;
                    found[index] = true;
                }

                if (node.Tags is { Count: > 0 } && seenNodes.Add(id) && Classify(node.Tags) is { } poi)
                {
                    pois.Add(new Poi(poi.Kind, poi.Name, poi.Category, nodeLat, nodeLon, poi.Importance));
                }
            }
        }

        foreach (var (poi, nodes) in wayCandidates.Values)
        {
            double sumLat = 0, sumLon = 0;
            var count = 0;
            foreach (var id in nodes.Distinct())
            {
                var index = Array.BinarySearch(nodeIds, id);
                if (index < 0 || !found[index]) continue;
                sumLat += lat[index];
                sumLon += lon[index];
                count++;
            }

            if (count > 0)
            {
                pois.Add(new Poi(poi.Kind, poi.Name, poi.Category, sumLat / count, sumLon / count, poi.Importance));
            }
        }

        // The same place is often mapped twice (a node on a building, the site around it)
        // or appears in two country extracts: keep one per name and ~1 km cell.
        var result = pois
            .GroupBy(p => (p.Kind, TextNormalizer.Key(p.Name), Math.Round(p.Lat, 2), Math.Round(p.Lon, 2)))
            .Select(g => g.First())
            .ToList();

        Console.WriteLine("  points of interest: " + string.Join(", ",
            result.GroupBy(p => p.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count():N0}")));
        return result;
    }

    private static Candidate? Classify(TagsCollectionBase? tags)
    {
        if (tags is null || tags.Count == 0) return null;
        string? V(string key) => tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        bool Is(string key, params string[] values) => V(key) is { } v && values.Contains(v, StringComparer.OrdinalIgnoreCase);

        var name = V("name");
        var forTrucks = Is("hgv", "yes", "designated", "only");

        if (Is("barrier", "border_control"))
        {
            return new Candidate(PlaceKind.BorderCrossing, name ?? "Granični prelaz", "granični prelaz", 5000);
        }

        if (Is("amenity", "fuel"))
        {
            var truckFuel = forTrucks || Is("fuel:HGV_diesel", "yes");
            return new Candidate(PlaceKind.Fuel, name ?? V("brand") ?? V("operator") ?? "Pumpa",
                truckFuel ? "pumpa za kamione" : "pumpa", truckFuel ? 1500 : 1000);
        }

        if (Is("highway", "services", "rest_area"))
        {
            return new Candidate(PlaceKind.TruckStop, name ?? "Odmorište", "odmorište", 2000);
        }

        if (Is("amenity", "parking") && (forTrucks || Is("parking", "truck_stop")))
        {
            return new Candidate(PlaceKind.TruckParking, name ?? "Parking za kamione", "parking za kamione", 1500);
        }

        // Companies, factories and warehouses are only useful with a name to search for.
        if (name is null) return null;

        if (Is("building", "warehouse") || Is("industrial", "warehouse", "logistics") || Is("office", "logistics"))
        {
            return new Candidate(PlaceKind.Warehouse, name, "skladište", 500);
        }

        if (Is("man_made", "works") || V("industrial") is not null || Is("building", "industrial"))
        {
            return new Candidate(PlaceKind.Factory, name, "fabrika", 500);
        }

        if (Is("landuse", "industrial"))
        {
            return new Candidate(PlaceKind.Factory, name, "industrijska zona", 500);
        }

        if (Is("office", "company") || Is("shop", "wholesale", "trade"))
        {
            return new Candidate(PlaceKind.Company, name, "firma", 500);
        }

        return null;
    }
}
