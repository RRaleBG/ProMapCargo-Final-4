using System.Text.Json;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Routing;

/// <summary>
/// Turns OSRM route legs (requested with steps=true) into the same maneuver list
/// the PostGIS engine returns, so the fallback route still has turn-by-turn guidance.
/// </summary>
public static class OsrmManeuverBuilder
{
    public static IReadOnlyList<RouteManeuverDto> Build(object? legs)
    {
        if (legs is not JsonElement legsElement || legsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<RouteManeuverDto>();
        double cumulative = 0;

        foreach (var leg in legsElement.EnumerateArray())
        {
            if (!leg.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var step in steps.EnumerateArray())
            {
                if (!step.TryGetProperty("maneuver", out var maneuver))
                {
                    continue;
                }

                var osrmType = GetString(maneuver, "type") ?? "";
                var modifier = GetString(maneuver, "modifier") ?? "";
                var distance = step.TryGetProperty("distance", out var d) && d.TryGetDouble(out var dv) ? dv : 0;
                var roadName = NullIfEmpty(GetString(step, "name"));
                var roadRef = NullIfEmpty(GetString(step, "ref"));
                int? exit = maneuver.TryGetProperty("exit", out var e) && e.TryGetInt32(out var ev) ? ev : null;

                double lat = 0, lon = 0;
                if (maneuver.TryGetProperty("location", out var location) && location.GetArrayLength() >= 2)
                {
                    lon = location[0].GetDouble();
                    lat = location[1].GetDouble();
                }

                var type = ToType(osrmType, modifier);

                // "new name" and plain "continue straight" steps only rename the road; skip them
                // unless they are the first or last step, so guidance stays focused on real turns.
                if (type == "Continue" && result.Count > 0 && osrmType is "new name" or "continue")
                {
                    cumulative += distance;
                    continue;
                }

                var road = roadName ?? roadRef;
                result.Add(new RouteManeuverDto(
                    result.Count,
                    type,
                    ToInstruction(type, road, exit),
                    result.Count == 0 ? 0 : distance,
                    cumulative,
                    lat,
                    lon,
                    roadName,
                    roadRef,
                    exit));

                cumulative += distance;
            }
        }

        return result;
    }

    private static string ToType(string osrmType, string modifier)
    {
        switch (osrmType)
        {
            case "depart":
                return "Depart";
            case "arrive":
                return "Arrive";
            case "roundabout":
            case "rotary":
            case "roundabout turn":
            case "exit roundabout":
            case "exit rotary":
                return "Roundabout";
        }

        return modifier switch
        {
            "left" => "Left",
            "right" => "Right",
            "slight left" => "Slight-Left",
            "slight right" => "Slight-Right",
            "sharp left" => "Sharp-Left",
            "sharp right" => "Sharp-Right",
            "uturn" => "U-Turn",
            _ => "Continue"
        };
    }

    private static string ToInstruction(string type, string? road, int? exit)
    {
        var on = string.IsNullOrWhiteSpace(road) ? "" : $" na {road}";

        return type switch
        {
            "Depart" => string.IsNullOrWhiteSpace(road) ? "Krenite" : $"Krenite na {road}",
            "Arrive" => "Stigli ste",
            "Left" => $"Skrenite levo{on}",
            "Right" => $"Skrenite desno{on}",
            "Slight-Left" => $"Držite se levo{on}",
            "Slight-Right" => $"Držite se desno{on}",
            "Sharp-Left" => $"Oštro levo{on}",
            "Sharp-Right" => $"Oštro desno{on}",
            "U-Turn" => "Okrenite se polukružno",
            "Roundabout" => exit is > 0
                ? $"U kružnom toku izađite na {exit}. izlaz{on}"
                : $"Uđite u kružni tok{on}",
            _ => string.IsNullOrWhiteSpace(road) ? "Nastavite pravo" : $"Nastavite na {road}"
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
