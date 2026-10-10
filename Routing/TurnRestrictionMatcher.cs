using System.Collections.Concurrent;
using Dapper;
using Npgsql;

namespace ProMapCargo.Api.Routing;

public sealed class TurnRestrictionMatcher(
    NpgsqlDataSource ds,
    ILogger<TurnRestrictionMatcher> logger)
{
    /*
     * Turn restrictions never change for a given graph_version, so they
     * are loaded once and shared by every request. This class must be
     * registered as a singleton.
     */
    private readonly ConcurrentDictionary<long, Task<RestrictionSet>> cache = new();

    public async Task<RestrictionSet> LoadAsync(long version, CancellationToken ct)
    {
        var task = cache.GetOrAdd(version, v => LoadFromDatabaseAsync(v));

        try
        {
            var set = await task.WaitAsync(ct);

            // Drop sets of older graph versions once a newer one is in use.
            foreach (var key in cache.Keys)
            {
                if (key != version)
                {
                    cache.TryRemove(key, out _);
                }
            }

            return set;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do not cache failures.
            cache.TryRemove(new KeyValuePair<long, Task<RestrictionSet>>(version, task));
            throw;
        }
    }

    private async Task<RestrictionSet> LoadFromDatabaseAsync(long version)
    {
        logger.LogInformation("PostGIS query started: turn restriction load. GraphVersion={GraphVersion}", version);

        try
        {
            // Shared between requests: deliberately not tied to one request's token.
            await using var c = await ds.OpenConnectionAsync(CancellationToken.None);
            var rows = await c.QueryAsync<dynamic>(new CommandDefinition(
                "SELECT restriction,from_way_id,to_way_id,via_node_ids FROM turn_restrictions WHERE graph_version=@version",
                new
                {
                    version
                },
                commandTimeout: 120,
                cancellationToken: CancellationToken.None));

            var rules = rows.Select(r => new Rule(
                (string?)r.restriction,
                (long?)r.from_way_id,
                (long?)r.to_way_id,
                ((long[]?)r.via_node_ids) ?? [])).ToList();

            logger.LogInformation("PostGIS query completed: turn restriction load. GraphVersion={GraphVersion} Rules={Rules}", version, rules.Count);
            return new RestrictionSet(rules);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostGIS query failed: turn restriction load. GraphVersion={GraphVersion}", version);
            throw;
        }
    }
}

public sealed record Rule(string? Type, long? FromWay, long? ToWay, long[] ViaNodes);

public sealed class RestrictionSet
{
    private readonly Dictionary<long, List<Rule>> rulesByFromWay;

    public RestrictionSet(IReadOnlyList<Rule> rules)
    {
        rulesByFromWay = rules
            .Where(rule => rule.FromWay.HasValue)
            .GroupBy(rule => rule.FromWay!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    public bool Allows(long? previousWay, long nextWay, long node)
    {
        if (!previousWay.HasValue || !rulesByFromWay.TryGetValue(previousWay.Value, out var candidates))
        {
            return true;
        }

        foreach (var rule in candidates)
        {
            if (rule.ViaNodes.Length > 0 && Array.IndexOf(rule.ViaNodes, node) < 0)
            {
                continue;
            }

            if (string.Equals(rule.Type, "no_left_turn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.Type, "no_right_turn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.Type, "no_straight_on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.Type, "no_u_turn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rule.Type, "no_turn", StringComparison.OrdinalIgnoreCase))
            {
                if (rule.ToWay == nextWay)
                {
                    return false;
                }
            }

            if (rule.Type?.StartsWith("only_", StringComparison.OrdinalIgnoreCase) == true &&
                rule.ToWay != nextWay)
            {
                return false;
            }
        }

        return true;
    }
}
