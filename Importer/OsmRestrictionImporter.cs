using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;
using System.Globalization;

namespace ProMapCargo.OsmImporter;

public sealed class OsmRestrictionImporter(ImportDbContext db)
{
    private readonly GeometryFactory _factory = new(new PrecisionModel(), 4326);

    public async Task ImportAsync(string pbfPath)
    {
        var imported = 0L;
        var scanned = 0L;
        await using var fs = File.OpenRead(pbfPath);
        using var source = new PBFOsmStreamSource(fs);
        var batch = new List<RoadRestriction>(5000);
        foreach (var osm in source)
        {
            if (osm is not Way way || way.Tags is null) continue;
            scanned++;
            var tags = way.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            if (!LooksRelevant(tags)) continue;
            var restriction = Map(way, tags);
            if (restriction is null) continue;
            batch.Add(restriction);
            if (batch.Count >= 5000)
            {
                await db.RoadRestrictions.AddRangeAsync(batch);
                await db.SaveChangesAsync();
                imported += batch.Count;
                batch.Clear();
                Console.WriteLine($"Scanned: {scanned:n0}, imported: {imported:n0}");
            }
        }
        if (batch.Count > 0)
        {
            await db.RoadRestrictions.AddRangeAsync(batch);
            await db.SaveChangesAsync();
            imported += batch.Count;
        }
        Console.WriteLine($"Scanned ways: {scanned:n0}");
        Console.WriteLine($"Imported restriction records: {imported:n0}");
    }

    private static bool LooksRelevant(IReadOnlyDictionary<string, string> t)
    {
        string[] keys =
        [
            "maxheight", "maxweight", "maxweight:hgv", "maxwidth",
            "maxlength", "maxaxleload", "hgv", "goods", "hazmat",
            "hazmat:class", "hazmat:adr_class", "access",
            "vehicle", "motor_vehicle"
        ];
        return keys.Any(t.ContainsKey);
    }

    private RoadRestriction? Map(Way way, IReadOnlyDictionary<string, string> t)
    {
        var type = DetectType(t);
        if (type is null) return null;
        return new RoadRestriction
        {
            OsmWayId = way.Id ?? 0L,
            Name = Get(t, "name") ?? "",
            RestrictionType = type,
            MaxWeightTons = ParseTons(Get(t, "maxweight:hgv") ?? Get(t, "maxweight")),
            MaxHeightMeters = ParseMeters(Get(t, "maxheight")),
            MaxWidthMeters = ParseMeters(Get(t, "maxwidth")),
            MaxLengthMeters = ParseMeters(Get(t, "maxlength")),
            MaxAxleLoadTons = ParseTons(Get(t, "maxaxleload")),
            HgvBan = IsBan(Get(t, "hgv")) || IsBan(Get(t, "goods")),
            HazmatBan = IsBan(Get(t, "hazmat")),
            AdrClass = Get(t, "hazmat:class") ?? Get(t, "hazmat:adr_class"),
            ConditionalRaw = Get(t, "maxheight:conditional")
            ?? Get(t, "maxweight:conditional")
            ?? Get(t, "hgv:conditional"),
            Geometry = _factory.CreatePoint(new Coordinate(0, 0))
        };
    }

    private static string? DetectType(IReadOnlyDictionary<string, string> t)
    {
        if (t.ContainsKey("maxheight")) return "maxheight";
        if (t.ContainsKey("maxweight") || t.ContainsKey("maxweight:hgv")) return "maxweight";
        if (t.ContainsKey("maxwidth")) return "maxwidth";
        if (t.ContainsKey("maxlength")) return "maxlength";
        if (t.ContainsKey("maxaxleload")) return "maxaxleload";
        if (t.ContainsKey("hgv") || t.ContainsKey("goods")) return "hgv";
        if (t.ContainsKey("hazmat") || t.ContainsKey("hazmat:class")) return "hazmat";
        return null;
    }

    private static string? Get(IReadOnlyDictionary<string, string> t, string key)
    => t.TryGetValue(key, out var v) ? v : null;

    private static bool IsBan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("agricultural", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? ParseMeters(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim().Replace(',', '.');
        var numeric = new string(raw.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());

        if (!decimal.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        if (raw.Contains("ft", StringComparison.OrdinalIgnoreCase))
            value *= 0.3048m;

        return value;
    }

    private static decimal? ParseTons(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        raw = raw.Trim().Replace(',', '.');
        var numeric = new string(raw.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        if (!decimal.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        if (raw.Contains("kg", StringComparison.OrdinalIgnoreCase))
            value /= 1000m;
        return value;
    }
}