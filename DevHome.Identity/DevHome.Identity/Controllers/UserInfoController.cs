using DevHome.Identity.DataContext.Models;
using DevHome.Identity.OpenIddict;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DevHome.Identity.Controllers;

/// <summary>
/// UserInfo-эндпоинт OpenID Connect: возвращает данные пользователя по access-токену.
/// </summary>
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class UserInfoController(UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    public async Task<IActionResult> UserInfo()
    {
        var subject = User.GetClaim(Claims.Subject);
        if (string.IsNullOrEmpty(subject))
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(subject);
        if (user is null)
        {
            return NotFound();
        }

        var result = new Dictionary<string, object?>
        {
            [Claims.Subject] = subject,
            [Claims.Name] = await userManager.GetUserNameAsync(user),
            [Claims.Email] = await userManager.GetEmailAsync(user)
        };

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count > 0)
        {
            result[Claims.Role] = roles;
        }

        return Ok(result);
    }
}
