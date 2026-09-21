using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Services;
public interface IRoutingService
{
    Task<OsrmResponse> RouteAsync(RouteRequest request, CancellationToken ct);
}
public sealed class OsrmResponse
{
    public string Code { get; init; } = "";
    public List<OsrmRoute> Routes { get; init; } = [];
}
public sealed class OsrmRoute
{
    public double Distance { get; init; }
    public double Duration { get; init; }
    public object? Geometry { get; init; }
    public object? Legs { get; init; }
}
