using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models
{
    public sealed class VehicleProfile
    {
        [JsonPropertyName("heightMeters")]
        public double HeightMeters { get; init; } = 4.0;

        [JsonPropertyName("widthMeters")]
        public double WidthMeters { get; init; } = 2.55;

        [JsonPropertyName("lengthMeters")]
        public double LengthMeters { get; init; } = 16.5;

        [JsonPropertyName("weightTons")]
        public double WeightTons { get; init; } = 40.0;
    }
}
