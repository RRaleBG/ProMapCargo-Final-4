using System.Globalization;

namespace ProMapCargo.Mobile.Converters;

/// <summary>True ↔ false (show a section when a flag is off, e.g. the catalog while online).</summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}
