using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public interface IGeocodingService
{
    Task<IReadOnlyList<object>> SearchAsync(string query, int limit, CancellationToken ct);
}