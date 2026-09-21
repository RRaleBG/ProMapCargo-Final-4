using Dapper;
using Npgsql;
namespace ProMapCargo.Api.Routing;
public sealed class TurnRestrictionMatcher(NpgsqlDataSource ds)
{
    public async Task<RestrictionSet> LoadAsync(long version,CancellationToken ct) {
        await using var c=await ds.OpenConnectionAsync(ct);
        var rows=await c.QueryAsync<dynamic>(new CommandDefinition("SELECT restriction,from_way_id,to_way_id,via_node_ids FROM turn_restrictions WHERE graph_version=@version",new {
            version
        }
        ,cancellationToken:ct));
        return new RestrictionSet(rows.Select(r=>new
            Rule((string?)r.restriction,(long?)r.from_way_id,(long?)r.to_way_id,((long[]?)r.via_node_ids)??[])).ToList());
    }
}
public sealed record Rule(string? Type,long? FromWay,long? ToWay,long[] ViaNodes);
public sealed class RestrictionSet(IReadOnlyList<Rule> rules)
{
    public bool Allows(long? previousWay,long nextWay,long node) {
        foreach(var r in rules) {
            if(r.FromWay!=previousWay)continue;
            if(r.ViaNodes.Length>0&&!r.ViaNodes.Contains(node))continue;
            if(string.Equals(r.Type,"no_left_turn",StringComparison.OrdinalIgnoreCase)||string.Equals(r.Type,"no_right_turn",StringComparison.OrdinalIgnoreCase)||string.Equals(r.Type,"no_straight_on",StringComparison.OrdinalIgnoreCase)||string.Equals(r.Type,"no_u_turn",StringComparison.OrdinalIgnoreCase)||string.Equals(r.Type,"no_turn",StringComparison.OrdinalIgnoreCase)) {
                if(r.ToWay==nextWay)return false;
            }
            if(r.Type?.StartsWith("only_",StringComparison.OrdinalIgnoreCase)==true&&r.ToWay!=nextWay)return false;
        }
        return true;
    }
}
