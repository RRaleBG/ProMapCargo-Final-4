using System.Runtime.InteropServices;
using System.Text;

namespace ProMapCargo.OfflineRouting;

/// <summary>
/// The offline road network for one region, as written by the bundle builder and
/// read on the phone (*.graphbundle). Prepared for the default truck profile: roads
/// a 40 t / 4 m / 2.55 m / 16.5 m truck may not use are already left out, and the
/// graph is contracted (Contraction Hierarchies) so a query takes milliseconds.
///
/// Arcs are directed: original road arcs plus shortcuts. Each arc u→v is stored once,
/// in the "up" list of u when v ranks higher, else in the "down" list of v (searched
/// backwards from the destination).
/// </summary>
public sealed class GraphBundle
{
    public const int FormatVersion = 1;
    private static readonly byte[] Magic = "PMGB"u8.ToArray();

    public long GraphVersion { get; set; }
    public DateTime BuiltAtUtc { get; set; }

    // Junctions, microdegrees.
    public int[] NodeLat { get; set; } = [];
    public int[] NodeLon { get; set; } = [];

    // Road segments between junctions (geometry and naming carriers).
    public int[] RoadFrom { get; set; } = [];
    public int[] RoadTo { get; set; } = [];
    /// <summary>1 = only from→to, -1 = only to→from, 0 = both (as road_edges.direction).</summary>
    public sbyte[] RoadDirection { get; set; } = [];
    /// <summary>Travel time in deciseconds for the truck profile.</summary>
    public int[] RoadWeight { get; set; } = [];
    public float[] RoadLength { get; set; } = [];
    public int[] RoadName { get; set; } = [];
    public int[] RoadRef { get; set; } = [];
    public byte[] RoadClass { get; set; } = [];
    public long[] RoadGeometryOffset { get; set; } = [];
    public int[] RoadPointCount { get; set; } = [];

    public string[] Strings { get; set; } = [];

    /// <summary>Zigzag-varint delta-encoded microdegree points (lat, lon) per road.</summary>
    public byte[] Geometry { get; set; } = [];

    // Arcs for unpacking: original arcs have ArcA = -(road*2 + backward) - 1, ArcB = -1;
    // shortcuts reference their two child arcs.
    public int[] ArcA { get; set; } = [];
    public int[] ArcB { get; set; } = [];

    // Upward search graph (CSR): neighbour, weight (deciseconds) and arc id.
    public int[] UpOffsets { get; set; } = [];
    public int[] UpNode { get; set; } = [];
    public int[] UpWeight { get; set; } = [];
    public int[] UpArc { get; set; } = [];

    // Backward upward graph (CSR): for node v the arcs u→v with rank(u) > rank(v).
    public int[] DownOffsets { get; set; } = [];
    public int[] DownNode { get; set; } = [];
    public int[] DownWeight { get; set; } = [];
    public int[] DownArc { get; set; } = [];

    // Snapping grid: roads whose bounding box touches each cell.
    public double GridMinLat { get; set; }
    public double GridMinLon { get; set; }
    public double GridCellDegrees { get; set; }
    public int GridColumns { get; set; }
    public int GridRows { get; set; }
    public int[] GridOffsets { get; set; } = [];
    public int[] GridRoads { get; set; } = [];

    public int NodeCount => NodeLat.Length;
    public int RoadCount => RoadFrom.Length;

    public string? StringAt(int index) => index >= 0 && index < Strings.Length ? Strings[index] : null;

    /// <summary>Points of a road in stored order (from → to), microdegrees.</summary>
    public void ReadRoadPoints(int road, List<(int Lat, int Lon)> target)
    {
        target.Clear();
        var offset = (int)RoadGeometryOffset[road];
        var count = RoadPointCount[road];
        int lat = 0, lon = 0;
        for (var i = 0; i < count; i++)
        {
            lat += ReadZigZag(Geometry, ref offset);
            lon += ReadZigZag(Geometry, ref offset);
            target.Add((lat, lon));
        }
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(GraphVersion);
        writer.Write(BuiltAtUtc.ToBinary());

        WriteArray(writer, NodeLat);
        WriteArray(writer, NodeLon);

        WriteArray(writer, RoadFrom);
        WriteArray(writer, RoadTo);
        WriteArray(writer, RoadDirection);
        WriteArray(writer, RoadWeight);
        WriteArray(writer, RoadLength);
        WriteArray(writer, RoadName);
        WriteArray(writer, RoadRef);
        WriteArray(writer, RoadClass);
        WriteArray(writer, RoadGeometryOffset);
        WriteArray(writer, RoadPointCount);

        writer.Write(Strings.Length);
        foreach (var value in Strings)
        {
            writer.Write(value);
        }

        WriteArray(writer, Geometry);

        WriteArray(writer, ArcA);
        WriteArray(writer, ArcB);

        WriteArray(writer, UpOffsets);
        WriteArray(writer, UpNode);
        WriteArray(writer, UpWeight);
        WriteArray(writer, UpArc);
        WriteArray(writer, DownOffsets);
        WriteArray(writer, DownNode);
        WriteArray(writer, DownWeight);
        WriteArray(writer, DownArc);

        writer.Write(GridMinLat);
        writer.Write(GridMinLon);
        writer.Write(GridCellDegrees);
        writer.Write(GridColumns);
        writer.Write(GridRows);
        WriteArray(writer, GridOffsets);
        WriteArray(writer, GridRoads);
    }

    public static GraphBundle Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Nije ProMap paket za rutiranje.");
        }

        var version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Nepodržana verzija paketa za rutiranje: {version}.");
        }

        var bundle = new GraphBundle
        {
            GraphVersion = reader.ReadInt64(),
            BuiltAtUtc = DateTime.FromBinary(reader.ReadInt64()),
            NodeLat = ReadArray<int>(reader),
            NodeLon = ReadArray<int>(reader),
            RoadFrom = ReadArray<int>(reader),
            RoadTo = ReadArray<int>(reader),
            RoadDirection = ReadArray<sbyte>(reader),
            RoadWeight = ReadArray<int>(reader),
            RoadLength = ReadArray<float>(reader),
            RoadName = ReadArray<int>(reader),
            RoadRef = ReadArray<int>(reader),
            RoadClass = ReadArray<byte>(reader),
            RoadGeometryOffset = ReadArray<long>(reader),
            RoadPointCount = ReadArray<int>(reader),
        };

        var stringCount = reader.ReadInt32();
        var strings = new string[stringCount];
        for (var i = 0; i < stringCount; i++)
        {
            strings[i] = reader.ReadString();
        }

        bundle.Strings = strings;
        bundle.Geometry = ReadArray<byte>(reader);
        bundle.ArcA = ReadArray<int>(reader);
        bundle.ArcB = ReadArray<int>(reader);
        bundle.UpOffsets = ReadArray<int>(reader);
        bundle.UpNode = ReadArray<int>(reader);
        bundle.UpWeight = ReadArray<int>(reader);
        bundle.UpArc = ReadArray<int>(reader);
        bundle.DownOffsets = ReadArray<int>(reader);
        bundle.DownNode = ReadArray<int>(reader);
        bundle.DownWeight = ReadArray<int>(reader);
        bundle.DownArc = ReadArray<int>(reader);
        bundle.GridMinLat = reader.ReadDouble();
        bundle.GridMinLon = reader.ReadDouble();
        bundle.GridCellDegrees = reader.ReadDouble();
        bundle.GridColumns = reader.ReadInt32();
        bundle.GridRows = reader.ReadInt32();
        bundle.GridOffsets = ReadArray<int>(reader);
        bundle.GridRoads = ReadArray<int>(reader);
        return bundle;
    }

    public static void WriteZigZag(List<byte> target, int value)
    {
        var zigzag = (uint)((value << 1) ^ (value >> 31));
        while (zigzag >= 0x80)
        {
            target.Add((byte)(zigzag | 0x80));
            zigzag >>= 7;
        }

        target.Add((byte)zigzag);
    }

    private static int ReadZigZag(byte[] buffer, ref int offset)
    {
        uint result = 0;
        var shift = 0;
        while (true)
        {
            var b = buffer[offset++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return (int)(result >> 1) ^ -(int)(result & 1);
    }

    private static void WriteArray<T>(BinaryWriter writer, T[] values) where T : unmanaged
    {
        writer.Write(values.Length);
        writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static T[] ReadArray<T>(BinaryReader reader) where T : unmanaged
    {
        var length = reader.ReadInt32();
        var values = new T[length];
        var bytes = MemoryMarshal.AsBytes(values.AsSpan());
        var read = 0;
        while (read < bytes.Length)
        {
            var n = reader.BaseStream.Read(bytes[read..]);
            if (n == 0)
            {
                throw new EndOfStreamException("Paket za rutiranje je nepotpun.");
            }

            read += n;
        }

        return values;
    }
}
