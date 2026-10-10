using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace ProMapCargo.Mobile.Converters;

/// <summary>
/// Maps a maneuver type (as sent by the routing API: left, slight-right, roundabout, arrive…)
/// to a 24×24 vector arrow for the TomTom-style guidance banner.
/// </summary>
public sealed class ManeuverIconConverter : IValueConverter
{
    // Right-hand traffic: u-turns go to the left. "F0" = even-odd fill for shapes with holes.
    private static readonly Dictionary<string, string> PathData = new(StringComparer.OrdinalIgnoreCase)
    {
        ["continue"] = "M10.25 22 V9 H5.5 L12 1.5 L18.5 9 H13.75 V22 Z",
        ["right"] = "M6.5 22 V12 C6.5 8.96 8.96 6.5 12 6.5 H15 V2.5 L21.5 8.25 L15 14 V10 H12 C10.9 10 10 10.9 10 12 V22 Z",
        ["left"] = "M17.5 22 V12 C17.5 8.96 15.04 6.5 12 6.5 H9 V2.5 L2.5 8.25 L9 14 V10 H12 C13.1 10 14 10.9 14 12 V22 Z",
        ["slight-right"] = "M8 22 V13.6 L14.7 6.9 L12 4.2 H20.5 V12.7 L17.8 10 L11.5 16.3 V22 Z",
        ["slight-left"] = "M16 22 V13.6 L9.3 6.9 L12 4.2 H3.5 V12.7 L6.2 10 L12.5 16.3 V22 Z",
        ["sharp-right"] = "M6 22 V8 C6 5 8.2 3 11 3 C12.3 3 13.5 3.5 14.4 4.4 L18 8 L20.5 5.5 V14.5 H11.5 L14 12 L10.9 8.9 C10.6 8.6 10 8.7 10 9.2 V22 Z",
        ["sharp-left"] = "M18 22 V8 C18 5 15.8 3 13 3 C11.7 3 10.5 3.5 9.6 4.4 L6 8 L3.5 5.5 V14.5 H12.5 L10 12 L13.1 8.9 C13.4 8.6 14 8.7 14 9.2 V22 Z",
        ["u-turn"] = "M18 22 V8.5 C18 5.46 15.54 3 12.5 3 C9.46 3 7 5.46 7 8.5 V14 H3.5 L8.75 20.5 L14 14 H10.5 V8.5 C10.5 7.4 11.4 6.5 12.5 6.5 C13.6 6.5 14.5 7.4 14.5 8.5 V22 Z",
        ["roundabout"] = "F0 M10.6 22 V16.1 A4.6 4.6 0 1 1 13.4 16.1 V22 Z M12 9.4 A2.3 2.3 0 1 0 12 14 A2.3 2.3 0 1 0 12 9.4 Z M10.6 7.1 V5 H7.8 L12 1 L16.2 5 H13.4 V7.1 Z",
        ["arrive"] = "F0 M12 1.5 C7.86 1.5 4.5 4.86 4.5 9 C4.5 14.6 12 22.5 12 22.5 C12 22.5 19.5 14.6 19.5 9 C19.5 4.86 16.14 1.5 12 1.5 Z M12 6 A3 3 0 1 0 12 12 A3 3 0 1 0 12 6 Z",
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["turn-right"] = "right",
        ["turn-left"] = "left",
        ["uturn"] = "u-turn",
        ["destination"] = "arrive",
        ["depart"] = "continue",
        ["straight"] = "continue",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = (value as string)?.Trim() ?? "";
        if (Aliases.TryGetValue(kind, out var alias))
        {
            kind = alias;
        }

        if (!PathData.ContainsKey(kind))
        {
            kind = "continue";
        }

        // A new Geometry per element: the banner and the "then" icon must not share one instance.
        return (Geometry)new PathGeometryConverter().ConvertFromInvariantString(PathData[kind])!;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
