using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models
{
    public sealed class RouteOptions
    {
        [JsonPropertyName("avoidTolls")]
        public bool AvoidTolls { get; init; }

        [JsonPropertyName("avoidFerries")]
        public bool AvoidFerries { get; init; } = true;

        [JsonPropertyName("avoidLowClearance")]
        public bool AvoidLowClearance { get; init; } = true;
    }
}
