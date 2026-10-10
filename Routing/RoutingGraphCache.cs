using System.Collections.Concurrent;
using NetTopologySuite.Geometries;

namespace ProMapCargo.Api.Routing;

internal readonly record struct GraphTileKey(long Version, int X, int Y);

/// <summary>
/// One square piece of the routing graph. Holds the traversable options
/// of every node whose own coordinate lies inside the tile; the lists are
/// complete because every edge incident to such a node intersects the tile.
/// </summary>
internal sealed class GraphTile(Dictionary<long, IReadOnlyList<(RoadEdge Edge, bool Forward)>> nodes)
{
    public Dictionary<long, IReadOnlyList<(RoadEdge Edge, bool Forward)>> Nodes { get; } = nodes;
}

/// <summary>
/// Process-wide cache of the routing graph, split into 0.1 degree tiles
/// (roughly 8 x 11 km). A tile is read from PostGIS once with a single
/// bounding-box query and then served from memory to every request, so a
/// route through an already visited area needs no database round trips
/// while searching.
///
/// Memory is bounded by <c>RoutingGraphCache:MaxTiles</c> (default 120);
/// the least recently used tiles are dropped first. Tiles of other graph
/// versions are dropped as soon as a newer version is in use.
///
/// Must be registered as a singleton.
/// </summary>
public sealed class RoutingGraphCache(
    PostGisRoutingRepository repo,
    IConfiguration configuration,
    ILogger<RoutingGraphCache> logger)
{
    public const double TileSizeDegrees = 0.1;

    private const double BoundsEpsilon = 1e-7;
    private const int DefaultMaxTiles = 120;
    private const int MaxParallelTileLoads = 4;

    private const double MinPreloadPaddingDegrees = 0.04;
    private const double MaxPreloadPaddingDegrees = 0.30;

    private sealed class TileEntry(Lazy<Task<GraphTile>> load)
    {
        public Lazy<Task<GraphTile>> Load { get; } = load;

        public long LastUsedTicks;
    }

    private readonly int maxTiles =
        Math.Max(16, configuration.GetValue("RoutingGraphCache:MaxTiles", DefaultMaxTiles));

    private readonly ILogger<RoutingGraphCache> log = logger;

    private readonly ConcurrentDictionary<GraphTileKey, TileEntry> tiles = new();
    private readonly SemaphoreSlim loadGate = new(MaxParallelTileLoads);
    private readonly object evictionLock = new();

    public int MaxTiles => maxTiles;

    /// <summary>Creates a per-request view onto the cache for one graph version.</summary>
    public RoutingGraphView CreateView(long version) => new(this, version);

    internal static GraphTileKey KeyFor(long version, Coordinate c) =>
        new(
            version,
            (int)Math.Floor(c.X / TileSizeDegrees),
            (int)Math.Floor(c.Y / TileSizeDegrees));

    internal async Task<GraphTile> GetTileAsync(GraphTileKey key, CancellationToken ct)
    {
        var entry =
            tiles.GetOrAdd(
                key,
                k => new TileEntry(new Lazy<Task<GraphTile>>(() => LoadTileAsync(k))));

        Volatile.Write(ref entry.LastUsedTicks, Environment.TickCount64);

        try
        {
            var tile = await entry.Load.Value.WaitAsync(ct);
            EvictIfNeeded(key.Version);
            return tile;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never cache a failed load; the next request retries.
            tiles.TryRemove(new KeyValuePair<GraphTileKey, TileEntry>(key, entry));
            throw;
        }
    }

    private async Task<GraphTile> LoadTileAsync(GraphTileKey key)
    {
        // The load is shared by all requests, so it is deliberately not
        // tied to the cancellation token of the request that started it.
        await loadGate.WaitAsync();

        try
        {
            var minLon = key.X * TileSizeDegrees;
            var minLat = key.Y * TileSizeDegrees;

            var nodes =
                await repo.LoadAreaAdjacencyAsync(
                    minLon - BoundsEpsilon,
                    minLat - BoundsEpsilon,
                    minLon + TileSizeDegrees + BoundsEpsilon,
                    minLat + TileSizeDegrees + BoundsEpsilon,
                    key.Version,
                    CancellationToken.None);

            log.LogInformation(
                "Routing graph tile loaded. GraphVersion={GraphVersion} Tile={TileX},{TileY} Nodes={Nodes} CachedTiles={CachedTiles}",
                key.Version,
                key.X,
                key.Y,
                nodes.Count,
                tiles.Count);

            return new GraphTile(nodes);
        }
        finally
        {
            loadGate.Release();
        }
    }

    private void EvictIfNeeded(long activeVersion)
    {
        if (tiles.Count <= maxTiles)
        {
            return;
        }

        lock (evictionLock)
        {
            foreach (var pair in tiles)
            {
                if (pair.Key.Version != activeVersion)
                {
                    tiles.TryRemove(pair);
                }
            }

            var excess = tiles.Count - maxTiles;

            if (excess <= 0)
            {
                return;
            }

            var victims =
                tiles
                    .Where(pair =>
                        pair.Value.Load.IsValueCreated &&
                        pair.Value.Load.Value.IsCompleted)
                    .OrderBy(pair => Volatile.Read(ref pair.Value.LastUsedTicks))
                    .Take(excess)
                    .ToList();

            foreach (var victim in victims)
            {
                tiles.TryRemove(victim);
            }

            log.LogInformation(
                "Routing graph cache evicted {Evicted} tiles. CachedTiles={CachedTiles} MaxTiles={MaxTiles}",
                victims.Count,
                tiles.Count,
                maxTiles);
        }
    }

    /// <summary>
    /// Per-request view. Keeps its own references to the tiles it used so a
    /// request never reloads a tile that the shared cache evicted meanwhile.
    /// Not thread safe; one view belongs to one request.
    /// </summary>
    public sealed class RoutingGraphView
    {
        private readonly RoutingGraphCache cache;
        private readonly long version;
        private readonly Dictionary<GraphTileKey, GraphTile> local = new();

        internal RoutingGraphView(RoutingGraphCache cache, long version)
        {
            this.cache = cache;
            this.version = version;
        }

        /// <summary>
        /// Loads, in parallel, the tiles covering the start/destination box
        /// padded by 30% of its longer side (0.04 to 0.30 degrees). Skipped
        /// when the box needs more tiles than the cache can hold; those
        /// routes load tiles on demand while searching. Failures are not
        /// fatal: the search loads missing tiles on demand.
        /// </summary>
        public async Task PreloadAsync(Coordinate a, Coordinate b, CancellationToken ct)
        {
            var minLon = Math.Min(a.X, b.X);
            var maxLon = Math.Max(a.X, b.X);
            var minLat = Math.Min(a.Y, b.Y);
            var maxLat = Math.Max(a.Y, b.Y);

            var padding =
                Math.Clamp(
                    0.30 * Math.Max(maxLon - minLon, maxLat - minLat),
                    MinPreloadPaddingDegrees,
                    MaxPreloadPaddingDegrees);

            var minX = (int)Math.Floor((minLon - padding) / TileSizeDegrees);
            var maxX = (int)Math.Floor((maxLon + padding) / TileSizeDegrees);
            var minY = (int)Math.Floor((minLat - padding) / TileSizeDegrees);
            var maxY = (int)Math.Floor((maxLat + padding) / TileSizeDegrees);

            var tileCount = (long)(maxX - minX + 1) * (maxY - minY + 1);

            if (tileCount > cache.maxTiles)
            {
                return;
            }

            var keys = new List<GraphTileKey>((int)tileCount);

            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    var key = new GraphTileKey(version, x, y);

                    if (!local.ContainsKey(key))
                    {
                        keys.Add(key);
                    }
                }
            }

            try
            {
                var loaded =
                    await Task.WhenAll(
                        keys.Select(async key =>
                            (Key: key, Tile: await cache.GetTileAsync(key, ct))));

                foreach (var (key, tile) in loaded)
                {
                    local[key] = tile;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cache.log.LogWarning(
                    ex,
                    "Routing graph preload failed; tiles will be loaded on demand. GraphVersion={GraphVersion}",
                    version);
            }
        }

        /// <summary>
        /// Traversable options of a node, loading its tile on first use.
        /// <paramref name="coordinate"/> must be the node's own coordinate.
        /// </summary>
        public async ValueTask<IReadOnlyList<(RoadEdge Edge, bool Forward)>> GetOutgoingAsync(
            long node,
            Coordinate coordinate,
            CancellationToken ct)
        {
            var key = KeyFor(version, coordinate);

            if (!local.TryGetValue(key, out var tile))
            {
                tile = await cache.GetTileAsync(key, ct);
                local[key] = tile;
            }

            return tile.Nodes.TryGetValue(node, out var options)
                ? options
                : [];
        }
    }
}
