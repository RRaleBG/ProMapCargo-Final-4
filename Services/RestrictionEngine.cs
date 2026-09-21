using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class RestrictionEngine : IRestrictionEngine
{
    public Task<IReadOnlyList<RestrictionViolation>> AnalyzeAsync(
        IEnumerable<GeoPoint> routePoints,
        TruckProfile? truck,
        DateTimeOffset? departureAt,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        /*
         * Ovaj engine trenutno predstavlja lokalni fallback
         * restriction engine.
         *
         * Pravo čitanje aktivnih ograničenja iz PostGIS-a
         * radi PostgresRestrictionEngine.
         *
         * Ovde vraćamo praznu listu dok se ne prosledi
         * konkretan restriction provider.
         */

        IReadOnlyList<RestrictionViolation> result = [];

        return Task.FromResult(result);
    }
}