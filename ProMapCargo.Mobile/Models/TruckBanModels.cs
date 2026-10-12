using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models;

/// <summary>Approved truck driving bans (GET /api/truck-bans); times are local to each country.</summary>
public sealed record TruckBanCalendar(
    [property: JsonPropertyName("countries")] IReadOnlyList<TruckBanCountry> Countries,
    [property: JsonPropertyName("bans")] IReadOnlyList<TruckBanItem> Bans);

public sealed record TruckBanCountry(
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("countryName")] string CountryName,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("minWeightT")] decimal? MinWeightT,
    [property: JsonPropertyName("nameSr")] string? NameSr);

public sealed record TruckBanItem(
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("countryName")] string CountryName,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("start")] string Start,
    [property: JsonPropertyName("end")] string End,
    [property: JsonPropertyName("regional")] bool Regional,
    [property: JsonPropertyName("info")] string Info);
