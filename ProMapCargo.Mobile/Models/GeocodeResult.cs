namespace ProMapCargo.Mobile.Models;

public sealed class GeocodeResult
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Type { get; set; }

    public string Title => DisplayName.Split(',')[0].Trim();
    public string Subtitle
    {
        get
        {
            var idx = DisplayName.IndexOf(',');
            return idx < 0 ? string.Empty : DisplayName[(idx + 1)..].Trim();
        }
    }
}
