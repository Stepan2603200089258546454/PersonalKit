using System.Security.Claims;
using Common.Security.OpenIddict.Server.Security;
using DevHome.Identity.DataContext.Models;
using DevHome.Identity.OpenIddict;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DevHome.Identity.Controllers;

/// <summary>
/// Конечные точки OpenID Connect: авторизация, выход и подтверждение кода устройства.
/// </summary>
/// <remarks>
/// OpenIddict настроен с passthrough (<c>EnableAuthorizationEndpointPassthrough</c> и другие),
/// поэтому проверку запроса и выдачу токенов делает он, а решения «пускать или нет»
/// и какие claim положить в токен — этот контроллер.
/// </remarks>
public sealed class AuthorizationController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOpenIddictApplicationManager applicationManager,
    IUserSecurityService userSecurityService,
    IOptions<UserSecurityOptions> securityOptions,
    ILogger<AuthorizationController> logger) : Controller
{
    /// <summary>
    /// Точка входа authorization code flow. Если пользователь не вошёл,
    /// перенаправляет на страницу входа Identity и возвращается сюда после неё.
    /// </summary>
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        if (HttpContext.GetOpenIddictServerRequest() is not { } request)
        {
            return BadRequest("Не удалось прочитать OpenID Connect-запрос.");
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        var userId = await userManager.GetUserIdAsync(user);
        var sessionId = ReadSessionId();
        if (sessionId is null)
        {
            // Cookie без sid — старая версия входа или вход до появления учёта
            // сеансов. Завершаем cookie и просим войти заново.
            await signInManager.SignOutAsync();
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        var validation = await userSecurityService.ValidateSessionAsync(userId, sessionId.Value);
        if (!validation.IsActive)
        {
            await signInManager.SignOutAsync();
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        var clientId = request.ClientId ?? "unknown";
        var application = await applicationManager.FindByClientIdAsync(clientId);
        var applicationName = application is null
            ? null
            : await applicationManager.GetDisplayNameAsync(application);

        await RecordApplicationSafeAsync(
            userId,
            sessionId.Value,
            clientId,
            applicationName,
            request.GetScopes());

        var principal = await CreatePrincipalAsync(user, request.GetScopes(), sessionId.Value);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Завершение сеанса: отзывает запись сеанса, удаляет cookie Identity
    /// и завершает сеанс OpenIddict.
    /// </summary>
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        await RevokeCurrentSessionAsync("Пользователь вышел через OpenID Connect.");
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await signInManager.SignOutAsync();

        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Страница подтверждения кода устройства. Пользователь должен быть аутентифицирован.
    /// </summary>
    [HttpGet("~/connect/verify")]
    public IActionResult Verify(string? user_code)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        ViewData["UserCode"] = user_code ?? string.Empty;
        return View();
    }

    /// <summary>
    /// Подтверждение кода устройства: OpenIddict связывает пользователя с ожидающим device code.
    /// </summary>
    [HttpPost("~/connect/verify")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Verify(string user_code, string action)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        if (!string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Подтверждение отклонено пользователем.");
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return BadRequest("Не удалось загрузить пользователя.");
        }

        var userId = await userManager.GetUserIdAsync(user);
        var sessionId = ReadSessionId();
        if (sessionId is null)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        var request = HttpContext.GetOpenIddictServerRequest();
        IEnumerable<string> scopes = request is null ? DevHomeScopes.User : request.GetScopes().ToArray();

        if (request?.ClientId is { Length: > 0 } clientId)
        {
            var application = await applicationManager.FindByClientIdAsync(clientId);
            var applicationName = application is null
                ? null
                : await applicationManager.GetDisplayNameAsync(application);

            await RecordApplicationSafeAsync(
                userId,
                sessionId.Value,
                clientId,
                applicationName,
                scopes);
        }

        var principal = await CreatePrincipalAsync(user, scopes, sessionId.Value);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task RecordApplicationSafeAsync(
        string userId,
        Guid sessionId,
        string clientId,
        string? applicationName,
        IEnumerable<string> scopes)
    {
        try
        {
            await userSecurityService.RecordApplicationAsync(
                userId,
                sessionId,
                clientId,
                applicationName,
                scopes,
                RequestContextInfo.FromHttpContext(
                    HttpContext,
                    securityOptions.Value.TrustForwardedHeaders));
        }
        catch (Exception exception)
        {
            // Сбой журналирования не должен ломать вход. Токен всё равно будет выдан,
            // а пропущенную запись можно увидеть по Warning в журнале приложения.
            logger.LogWarning(
                exception,
                "Не удалось записать приложение {ClientId} в сеанс {SessionId}.",
                clientId,
                sessionId);
        }
    }

    private Guid? ReadSessionId()
    {
        var value = User.FindFirstValue(OpenIddictSecurityClaimTypes.SessionId);
        return Guid.TryParse(value, out var sessionId) ? sessionId : null;
    }

    private async Task RevokeCurrentSessionAsync(string reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(Claims.Subject);
        var sessionId = ReadSessionId();

        if (string.IsNullOrWhiteSpace(userId) || sessionId is null)
        {
            return;
        }

        await userSecurityService.RevokeSessionAsync(userId, sessionId.Value, userId, reason);
    }

    /// <summary>
    /// Собирает набор claim пользователя для выдачи токена.
    /// </summary>
    private async Task<ClaimsPrincipal> CreatePrincipalAsync(
        ApplicationUser user,
        IEnumerable<string> scopes,
        Guid sessionId)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user));
        identity.SetClaim(Claims.Name, await userManager.GetUserNameAsync(user));

        // sid связывает токен с записью сеанса. По нему можно отозвать конкретный
        // вход и в будущем реализовать back-channel logout.
        identity.SetClaim(OpenIddictSecurityClaimTypes.SessionId, sessionId.ToString());

        var email = await userManager.GetEmailAsync(user);
        if (!string.IsNullOrEmpty(email))
        {
            identity.SetClaim(Claims.Email, email);
        }

        var roles = await userManager.GetRolesAsync(user);
        identity.SetClaims(Claims.Role, [.. roles]);

        var grantedScopes = scopes.ToList();
        identity.SetScopes(grantedScopes);

        if (grantedScopes.Contains(DevHomeScopes.Api, StringComparer.Ordinal))
        {
            identity.SetResources(DevHomeScopes.Api, DevHomeClients.Api);
        }

        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Определяет, в какие токены попадёт claim.
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Subject or Claims.Name or Claims.Email or Claims.Role
            or OpenIddictSecurityClaimTypes.SessionId =>
            [Destinations.AccessToken, Destinations.IdentityToken],

        _ => [Destinations.AccessToken]
    };
}
