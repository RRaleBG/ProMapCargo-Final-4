using Dapper;
using Npgsql;

namespace ProMapCargo.Api.Services;

public sealed record TruckBan(
    long Id,
    string CountryCode,
    string CountryName,
    DateTime BanDate,
    int StartMinute,
    int EndMinute,
    bool Regional,
    string Info,
    string Status,
    DateTime FirstSeenAt,
    DateTime? RemovedAtSource,
    string? ReviewedBy,
    DateTime? ReviewedAt);

public sealed record TruckBanCountry(
    string CountryCode,
    string CountryName,
    string TimeZone,
    decimal? MinWeightT,
    string? VehiclesText,
    bool NeedsReview);

/// <summary>Countries of the truckban.eu calendar: ISO code and time zone of the ban times.</summary>
public static class TruckBanCountries
{
    public static readonly IReadOnlyDictionary<string, (string Code, string TimeZone)> ByName =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Albania"] = ("AL", "Europe/Tirane"),
            ["Austria"] = ("AT", "Europe/Vienna"),
            ["Belarus"] = ("BY", "Europe/Minsk"),
            ["Belgium"] = ("BE", "Europe/Brussels"),
            ["Bosnia and Herzegovina"] = ("BA", "Europe/Sarajevo"),
            ["Bulgaria"] = ("BG", "Europe/Sofia"),
            ["Croatia"] = ("HR", "Europe/Zagreb"),
            ["Czech Republic"] = ("CZ", "Europe/Prague"),
            ["Denmark"] = ("DK", "Europe/Copenhagen"),
            ["Estonia"] = ("EE", "Europe/Tallinn"),
            ["Finland"] = ("FI", "Europe/Helsinki"),
            ["France"] = ("FR", "Europe/Paris"),
            ["Germany"] = ("DE", "Europe/Berlin"),
            ["Greece"] = ("GR", "Europe/Athens"),
            ["Hungary"] = ("HU", "Europe/Budapest"),
            ["Ireland"] = ("IE", "Europe/Dublin"),
            ["Italy"] = ("IT", "Europe/Rome"),
            ["Kosovo"] = ("XK", "Europe/Belgrade"),
            ["Latvia"] = ("LV", "Europe/Riga"),
            ["Liechtenstein"] = ("LI", "Europe/Vaduz"),
            ["Lithuania"] = ("LT", "Europe/Vilnius"),
            ["Luxembourg"] = ("LU", "Europe/Luxembourg"),
            ["Macedonia"] = ("MK", "Europe/Skopje"),
            ["Moldova"] = ("MD", "Europe/Chisinau"),
            ["Montenegro"] = ("ME", "Europe/Podgorica"),
            ["Netherlands"] = ("NL", "Europe/Amsterdam"),
            ["Norway"] = ("NO", "Europe/Oslo"),
            ["Poland"] = ("PL", "Europe/Warsaw"),
            ["Portugal"] = ("PT", "Europe/Lisbon"),
            ["Romania"] = ("RO", "Europe/Bucharest"),
            ["Serbia"] = ("RS", "Europe/Belgrade"),
            ["Slovakia"] = ("SK", "Europe/Bratislava"),
            ["Slovenia"] = ("SI", "Europe/Ljubljana"),
            ["Spain"] = ("ES", "Europe/Madrid"),
            ["Sweden"] = ("SE", "Europe/Stockholm"),
            ["Switzerland"] = ("CH", "Europe/Zurich"),
            ["Turkey"] = ("TR", "Europe/Istanbul"),
            ["Ukraine"] = ("UA", "Europe/Kyiv"),
            ["United Kingdom"] = ("GB", "Europe/London"),
        };
}

/// <summary>Storage of the truck bans (Sql/07-truck-bans.sql).</summary>
public sealed class TruckBanRepository(NpgsqlDataSource dataSource)
{
    private const string BanColumns = """
        id, country_code AS CountryCode, country_name AS CountryName, ban_date::timestamp AS BanDate,
        start_minute AS StartMinute, end_minute AS EndMinute, regional, info, status,
        first_seen_at AS FirstSeenAt, removed_at_source AS RemovedAtSource,
        reviewed_by AS ReviewedBy, reviewed_at AS ReviewedAt
        """;

    private const string CountryColumns = """
        country_code AS CountryCode, country_name AS CountryName, time_zone AS TimeZone,
        min_weight_t AS MinWeightT, vehicles_text AS VehiclesText, needs_review AS NeedsReview
        """;

    /// <summary>New bans arrive as pending; a ban seen again is only marked as seen.</summary>
    public async Task<int> UpsertSeenAsync(IReadOnlyList<(string Code, string Name, DateTime Date, int Start, int End, bool Regional, string Info, string InfoHash)> bans, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        const string count = "SELECT count(*) FROM truck_bans;";
        var before = await connection.ExecuteScalarAsync<long>(new CommandDefinition(count, transaction: transaction, cancellationToken: ct));
        foreach (var b in bans)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO truck_bans (country_code, country_name, ban_date, start_minute, end_minute, regional, info, info_hash)
                VALUES (@Code, @Name, @Date, @Start, @End, @Regional, @Info, @InfoHash)
                ON CONFLICT ON CONSTRAINT ux_truck_bans_identity
                DO UPDATE SET last_seen_at = now(), removed_at_source = NULL;
                """, new { b.Code, b.Name, Date = b.Date, b.Start, b.End, b.Regional, b.Info, b.InfoHash }, transaction, cancellationToken: ct));
        }

        var after = await connection.ExecuteScalarAsync<long>(new CommandDefinition(count, transaction: transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return (int)(after - before);
    }

    /// <summary>Bans of the synced days that the source no longer lists are flagged, not deleted.</summary>
    public async Task<int> FlagRemovedAsync(DateTime from, DateTime to, DateTime syncStartedUtc, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE truck_bans SET removed_at_source = now()
            WHERE ban_date BETWEEN @From AND @To AND last_seen_at < @Started
              AND removed_at_source IS NULL AND status <> 'rejected';
            """, new { From = from, To = to, Started = syncStartedUtc }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TruckBan>> GetForReviewAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<TruckBan>(new CommandDefinition($"""
            SELECT {BanColumns} FROM truck_bans
            -- past days need no decision (yesterday stays: a night ban may still run)
            WHERE ban_date >= current_date - 1
              AND (status = 'pending' OR (status = 'approved' AND removed_at_source IS NOT NULL))
            ORDER BY ban_date, country_name, start_minute;
            """, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TruckBan>> GetApprovedAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<TruckBan>(new CommandDefinition($"""
            SELECT {BanColumns} FROM truck_bans
            WHERE status = 'approved' AND ban_date BETWEEN @From AND @To
            ORDER BY ban_date, country_name, start_minute;
            """, new { From = from, To = to }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// approve: a pending ban goes live; for a live ban removed at the source it keeps it.
    /// reject: a pending ban is dropped; for a live ban removed at the source it confirms the removal.
    /// </summary>
    public async Task<int> ReviewAsync(IReadOnlyCollection<long> ids, bool approve, string reviewer, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE truck_bans
            SET status = CASE WHEN @Approve THEN 'approved' ELSE 'rejected' END,
                removed_at_source = CASE WHEN @Approve AND status = 'approved' THEN NULL ELSE removed_at_source END,
                reviewed_by = @Reviewer, reviewed_at = now()
            WHERE id = ANY(@Ids) AND (status = 'pending' OR (status = 'approved' AND removed_at_source IS NOT NULL));
            """, new { Ids = ids.ToArray(), Approve = approve, Reviewer = reviewer }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TruckBanCountry>> GetCountriesAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<TruckBanCountry>(new CommandDefinition(
            $"SELECT {CountryColumns} FROM truck_ban_countries ORDER BY country_name;", cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Stores the source's "Vehicles" text; a changed text needs a new review of the weight limit.</summary>
    public async Task UpsertCountryTextAsync(string code, string name, string timeZone, string vehiclesText, string textHash, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO truck_ban_countries (country_code, country_name, time_zone, vehicles_text, vehicles_text_hash)
            VALUES (@Code, @Name, @TimeZone, @Text, @Hash)
            ON CONFLICT (country_code) DO UPDATE SET
                country_name = EXCLUDED.country_name,
                time_zone = EXCLUDED.time_zone,
                vehicles_text = EXCLUDED.vehicles_text,
                vehicles_text_hash = EXCLUDED.vehicles_text_hash,
                needs_review = truck_ban_countries.needs_review OR truck_ban_countries.vehicles_text_hash IS DISTINCT FROM EXCLUDED.vehicles_text_hash,
                updated_at = now();
            """, new { Code = code, Name = name, TimeZone = timeZone, Text = vehiclesText, Hash = textHash }, cancellationToken: ct));
    }

    public async Task SetCountryWeightAsync(string code, decimal? minWeightT, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE truck_ban_countries SET min_weight_t = @Weight, needs_review = false, updated_at = now()
            WHERE country_code = @Code;
            """, new { Code = code, Weight = minWeightT }, cancellationToken: ct));
    }
}
