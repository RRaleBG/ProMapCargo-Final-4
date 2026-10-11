using System.Globalization;

namespace ProMapCargo.Api.Routing;

/// <summary>
/// Keeps the routing graph of the main area in memory: after start and whenever a new graph
/// version becomes active, the tiles of RoutingGraphCache:WarmUpBox ("minLon,minLat,maxLon,maxLat",
/// default Serbia; empty disables it) are loaded in the background. Without it the first long
/// route after a restart or an update waited for hundreds of tile queries (~10 s).
/// </summary>
public sealed class RoutingGraphWarmupService(
    RoutingGraphCache cache,
    PostGisRoutingRepository repository,
    IConfiguration configuration,
    ILogger<RoutingGraphWarmupService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var box = configuration.GetValue("RoutingGraphCache:WarmUpBox", "18.8,42.2,23.1,46.2") ?? "";
        var parts = box.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (parts.Length != 4)
        {
            return;
        }

        long? warmed = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var version = await repository.GetActiveGraphVersionAsync(stoppingToken);
                if (version is { } active && active != warmed)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var tiles = await cache.WarmUpAsync(active, parts[0], parts[1], parts[2], parts[3], stoppingToken);
                    warmed = active;
                    logger.LogInformation("Routing graph {Version} warmed up: {Tiles} tiles in {Seconds:0} s.", active, tiles, watch.Elapsed.TotalSeconds);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Routing graph warm-up failed; routes load tiles on demand.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
