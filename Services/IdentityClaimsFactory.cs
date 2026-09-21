using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Services;
public sealed class IdentityClaimsFactory(UserManager<ApplicationUser> userManager,RoleManager<ApplicationRole>
    roleManager,IOptions<IdentityOptions>
    options):UserClaimsPrincipalFactory<ApplicationUser,ApplicationRole>(userManager,roleManager,options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user) {
        var id=await base.GenerateClaimsAsync(user);
        if(user.CompanyId.HasValue)id.AddClaim(new Claim("company_id",user.CompanyId.Value.ToString()));
        if(!string.IsNullOrWhiteSpace(user.DisplayName))id.AddClaim(new Claim("display_name",user.DisplayName));
        return id;
    }
}
