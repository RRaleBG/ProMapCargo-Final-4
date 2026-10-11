using System.Text;

namespace ProMapCargo.OfflineRouting;

public enum PlaceKind : byte
{
    City = 0,
    Town = 1,
    Village = 2,
    Suburb = 3,
    Hamlet = 4,
    Street = 10,

    // Points of interest for trucks. New values only: older apps read them as plain places.
    Fuel = 20,
    TruckStop = 21,
    TruckParking = 22,
    BorderCrossing = 23,
    Company = 24,
    Factory = 25,
    Warehouse = 26,
}

public sealed record PlaceEntry(
    PlaceKind Kind,
    string Name,
    string Context,
    double Lat,
    double Lon,
    int Importance,
    string Key,
    string ContextKey)
{
    public static PlaceEntry Create(PlaceKind kind, string name, string context, double lat, double lon, int importance)
        => new(kind, name, context, lat, lon, importance, TextNormalizer.Key(name), TextNormalizer.Key(context));
}

public sealed record PlaceMatch(PlaceEntry Entry, double Score);

/// <summary>
/// Offline search for the route planner (*.places): cities, towns, villages and
/// streets of the region. Typed text is matched word by word as prefixes, in Latin
/// or Cyrillic, with or without diacritics. "Ulica, Mesto" narrows a street to a place.
/// Points of interest (fuel, truck stops and parking, border crossings, companies,
/// factories, warehouses) carry their category in the search key ("pumpa", "granični
/// prelaz"), so "pumpa, Novi Sad" finds the fuel stations of a town.
/// </summary>
public sealed class PlaceIndex
{
    public const int FormatVersion = 2;
    private static readonly byte[] Magic = "PMPL"u8.ToArray();

    public PlaceIndex(IReadOnlyList<PlaceEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<PlaceEntry> Entries { get; }

    public IReadOnlyList<PlaceMatch> Search(string query, int limit = 8, double? nearLat = null, double? nearLon = null)
    {
        var parts = query.Split(',', 2);
        var nameTokens = Tokens(parts[0]);
        var contextTokens = parts.Length > 1 ? Tokens(parts[1]) : [];
        if (nameTokens.Length == 0)
        {
            return [];
        }

        var fullKey = string.Join(' ', nameTokens);
        var matches = new List<PlaceMatch>();

        foreach (var entry in Entries)
        {
            if (!AllPrefixes(entry.Key, nameTokens))
            {
                continue;
            }

            if (contextTokens.Length > 0 && !AllPrefixes(entry.ContextKey, contextTokens))
            {
                continue;
            }

            var score = 0.0;
            if (entry.Key == fullKey)
            {
                score += 100;
            }
            else if (entry.Key.StartsWith(fullKey, StringComparison.Ordinal))
            {
                score += 40;
            }

            score += entry.Kind switch
            {
                PlaceKind.City => 60,
                PlaceKind.Town => 45,
                PlaceKind.Village => 20,
                PlaceKind.Suburb => 15,
                PlaceKind.Hamlet => 5,
                PlaceKind.BorderCrossing => 30,
                _ => 10,
            };

            score += Math.Log10(Math.Max(entry.Importance, 1)) * 5;

            if (nearLat is { } lat && nearLon is { } lon)
            {
                // Closer results first among similar matches (within ~100 km matters).
                var km = Math.Sqrt(Math.Pow((entry.Lat - lat) * 111, 2) + Math.Pow((entry.Lon - lon) * 78, 2));
                score -= Math.Min(km, 300) / 10;
            }

            matches.Add(new PlaceMatch(entry, score));
        }

        // One result per name and place: a border crossing is mapped once per direction,
        // a parking lot as a node and an area.
        return matches
            .OrderByDescending(match => match.Score)
            .DistinctBy(match => (match.Entry.Key, match.Entry.ContextKey))
            .Take(limit)
            .ToList();
    }

    private static string[] Tokens(string text)
        => TextNormalizer.Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool AllPrefixes(string key, string[] tokens)
    {
        foreach (var token in tokens)
        {
            var found = false;
            var start = 0;
            while (start <= key.Length - token.Length)
            {
                var index = key.IndexOf(token, start, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                if (index == 0 || key[index - 1] == ' ')
                {
                    found = true;
                    break;
                }

                start = index + 1;
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(Entries.Count);
        foreach (var entry in Entries)
        {
            writer.Write((byte)entry.Kind);
            writer.Write(entry.Name);
            writer.Write(entry.Context);
            writer.Write((int)Math.Round(entry.Lat * 1e6));
            writer.Write((int)Math.Round(entry.Lon * 1e6));
            writer.Write(entry.Importance);
            // Search keys stored so the phone does not normalise ~100k names on load.
            writer.Write(entry.Key);
            writer.Write(entry.ContextKey);
        }
    }

    public static PlaceIndex Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Nije ProMap indeks mesta.");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Nepodržana verzija indeksa mesta: {version}.");
        }

        var count = reader.ReadInt32();
        var entries = new List<PlaceEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var kind = (PlaceKind)reader.ReadByte();
            var name = reader.ReadString();
            var context = reader.ReadString();
            var lat = reader.ReadInt32() / 1e6;
            var lon = reader.ReadInt32() / 1e6;
            var importance = reader.ReadInt32();
            var key = reader.ReadString();
            var contextKey = reader.ReadString();
            entries.Add(new PlaceEntry(kind, name, context, lat, lon, importance, key, contextKey));
        }

        return new PlaceIndex(entries);
    }
}
