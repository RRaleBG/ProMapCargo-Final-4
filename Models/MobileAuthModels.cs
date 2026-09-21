using System.Text.Json.Serialization;

namespace ProMapCargo.Api.Models;

public sealed record MobileLoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password);

public sealed record MobileRefreshTokenRequest(
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

public sealed record MobileLoginResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("user")] MobileUserDto User);

public sealed record MobileUserDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("companyId")] Guid? CompanyId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles);
