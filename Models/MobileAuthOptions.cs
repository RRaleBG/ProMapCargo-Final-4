namespace ProMapCargo.Api.Models;

public sealed class MobileAuthOptions
{
    public string Issuer { get; set; } = "ProMapCargo";

    public string Audience { get; set; } = "ProMapCargo.Mobile";

    public string SigningKey { get; set; } = "promapcargo-mobile-change-this-signing-key-2026";

    public int AccessTokenMinutes { get; set; } = 480;

    public int RefreshTokenDays { get; set; } = 14;
}
