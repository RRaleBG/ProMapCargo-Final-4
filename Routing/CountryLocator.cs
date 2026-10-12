using System.Collections.Concurrent;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.IO;

namespace ProMapCargo.Api.Routing;

/// <summary>
/// Country of a coordinate, from the borders the map uses for truck bans
/// (wwwroot/data/truck-ban-countries.geojson, ~1 km precision). A 0.1 degree tile that lies
/// wholly inside one country is resolved once for every node in it; only border tiles test
/// each point. Must be registered as a singleton.
/// </summary>
public sealed class CountryLocator
{
    private const double TileDegrees = 0.1;
    private readonly STRtree<(string Code, IPreparedGeometry Shape)> index = new();
    private readonly ConcurrentDictionary<(int X, int Y), string?> interiorTiles = new();
    private readonly ConcurrentDictionary<(int X, int Y), bool> borderTiles = new();
    private readonly GeometryFactory factory = new(new PrecisionModel(), 4326);

    public CountryLocator(IWebHostEnvironment environment, ILogger<CountryLocator> logger)
    {
        var path = Path.Combine(environment.WebRootPath ?? "", "data", "truck-ban-countries.geojson");
        if (!File.Exists(path))
        {
            logger.LogWarning("Country borders not found at {Path}: truck bans cannot be applied to routes.", path);
            index.Build();
            return;
        }

        var features = new GeoJsonReader().Read<FeatureCollection>(File.ReadAllText(path));
        foreach (var feature in features)
        {
            if (feature.Attributes["code"] is string code && feature.Geometry is { IsEmpty: false } geometry)
            {
                index.Insert(geometry.EnvelopeInternal, (code, PreparedGeometryFactory.Prepare(geometry)));
            }
        }

        index.Build();
    }

    public string? CountryAt(Coordinate c)
    {
        var tile = ((int)Math.Floor(c.X / TileDegrees), (int)Math.Floor(c.Y / TileDegrees));
        if (interiorTiles.TryGetValue(tile, out var known))
        {
            return known;
        }

        if (!borderTiles.ContainsKey(tile))
        {
            // A tile wholly inside one country answers for all its points from now on.
            var envelope = new Envelope(tile.Item1 * TileDegrees, (tile.Item1 + 1) * TileDegrees, tile.Item2 * TileDegrees, (tile.Item2 + 1) * TileDegrees);
            var box = factory.ToGeometry(envelope);
            foreach (var (code, shape) in index.Query(envelope))
            {
                if (shape.Contains(box))
                {
                    interiorTiles[tile] = code;
                    return code;
                }
            }

            borderTiles[tile] = true;
        }

        var point = factory.CreatePoint(c);
        foreach (var (code, shape) in index.Query(point.EnvelopeInternal))
        {
            if (shape.Contains(point))
            {
                return code;
            }
        }

        return null;
    }
}
