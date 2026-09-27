using System.Collections.Concurrent;

namespace ProMapCargo.Api.Services;

public sealed class UserPresenceTracker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> connectionsByUser = new();

    public void SetOnline(Guid userId, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        var userConnections = connectionsByUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        userConnections[connectionId] = 1;
    }

    public void SetOffline(Guid userId, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);

        if (!connectionsByUser.TryGetValue(userId, out var userConnections))
        {
            return;
        }

        userConnections.TryRemove(connectionId, out _);

        if (userConnections.IsEmpty)
        {
            connectionsByUser.TryRemove(userId, out _);
        }
    }

    public bool IsOnline(Guid userId)
    {
        return connectionsByUser.TryGetValue(userId, out var userConnections) && !userConnections.IsEmpty;
    }

    public int OnlineCount => connectionsByUser.Count(x => !x.Value.IsEmpty);
}
