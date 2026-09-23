using Dapper;
using Npgsql;

namespace ProMapCargo.Api.Routing;

public sealed class TurnRestrictionMatcher(NpgsqlDataSource ds)
{
    public async Task<RestrictionSet> LoadAsync(long version, CancellationToken ct)
    {
        await using var c = await ds.OpenConnectionAsync(ct);
        var rows = await c.QueryAsync<dynamic>(new CommandDefinition(
            "SELECT restriction,from_way_id,to_way_id,via_node_ids FROM turn_restrictions WHERE graph_version=@version",
            new
            {
                version
            },
            cancellationToken: ct));

        return new RestrictionSet(rows.Select(r => new Rule(
            (string?)r.restriction,
            (long?)r.from_way_id,
            (long?)r.to_way_id,
            ((long[]?)r.via_node_ids) ?? [])).ToList());
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
