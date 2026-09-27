using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class IdentityClaimsFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options,
    ProMapCargoDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);

        if (user.CompanyId.HasValue)
        {
            identity.AddClaim(new Claim("company_id", user.CompanyId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim("display_name", user.DisplayName));
        }

        var roleIds = await db.UserRoles
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.RoleId)
            .ToListAsync()
            .ConfigureAwait(false);

        if (roleIds.Count > 0)
        {
            var permissions = await db.RolePermissions
                .AsNoTracking()
                .Where(x => roleIds.Contains(x.RoleId))
                .Select(x => x.Permission)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var permission in permissions)
            {
                if (!string.IsNullOrWhiteSpace(permission))
                {
                    identity.AddClaim(new Claim("permission", permission));
                }
            }
        }

        return identity;
    }
}
