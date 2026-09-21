using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Route("api/mobile-auth")]
public sealed class MobileAuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    MobileTokenService tokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<MobileLoginResponse>> Login([FromBody] MobileLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var normalizedEmail = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(normalizedEmail).ConfigureAwait(false)
                   ?? await userManager.FindByNameAsync(normalizedEmail).ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false).ConfigureAwait(false);
        if (!signInResult.Succeeded)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        return Ok(await tokenService.CreateAsync(user, ct).ConfigureAwait(false));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<MobileLoginResponse>> Refresh([FromBody] MobileRefreshTokenRequest request, CancellationToken ct)
    {
        var response = await tokenService.RefreshAsync(request.RefreshToken, ct).ConfigureAwait(false);
        if (response is null)
        {
            return Unauthorized(new { message = "Refresh token is invalid or expired." });
        }

        return Ok(response);
    }
}
