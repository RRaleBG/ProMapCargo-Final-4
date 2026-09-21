using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class MobileTokenService
{
    private readonly ProMapCargoDbContext db;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly MobileAuthOptions authOptions;

    public MobileTokenService(
        ProMapCargoDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<MobileAuthOptions> options)
    {
        this.db = db;
        this.userManager = userManager;
        authOptions = options.Value;
    }

    public async Task<MobileLoginResponse> CreateAsync(ApplicationUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        var roles = (await userManager.GetRolesAsync(user).ConfigureAwait(false)).ToArray();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(authOptions.AccessTokenMinutes);
        var refreshTokenValue = GenerateRefreshToken();
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(authOptions.RefreshTokenDays);

        await RevokeActiveRefreshTokensAsync(user.Id, ct).ConfigureAwait(false);

        db.MobileRefreshTokens.Add(new MobileRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAt = refreshExpiresAt
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new MobileLoginResponse(
            CreateAccessToken(user, roles, expiresAt),
            expiresAt,
            refreshTokenValue,
            new MobileUserDto(
                user.Id,
                user.CompanyId,
                user.Email ?? string.Empty,
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName ?? user.Email ?? string.Empty : user.DisplayName,
                roles));
    }

    public async Task<MobileLoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var existingToken = await db.MobileRefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == refreshToken, ct)
            .ConfigureAwait(false);

        if (existingToken is null || existingToken.User is null)
        {
            return null;
        }

        if (existingToken.RevokedAt.HasValue || existingToken.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        existingToken.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return await CreateAsync(existingToken.User, ct).ConfigureAwait(false);
    }

    private async Task RevokeActiveRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var tokens = await db.MobileRefreshTokens
            .Where(x => x.UserId == userId && !x.RevokedAt.HasValue && x.ExpiresAt > now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
    }

    private string CreateAccessToken(ApplicationUser user, IReadOnlyList<string> roles, DateTimeOffset expiresAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty)
        };

        if (user.CompanyId.HasValue)
        {
            claims.Add(new Claim("company_id", user.CompanyId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            claims.Add(new Claim("display_name", user.DisplayName));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: authOptions.Issuer,
            audience: authOptions.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
