using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Models
{
    public sealed record LoginRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password);

    public sealed record LoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("user")] MobileUser User);

    public sealed record MobileUser(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("companyId")] Guid? CompanyId,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles);

    public sealed record RefreshTokenRequest(
        [property: JsonPropertyName("refreshToken")] string RefreshToken);
}
