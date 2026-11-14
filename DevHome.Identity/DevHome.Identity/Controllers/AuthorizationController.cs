using System.Security.Claims;
using DevHome.Identity.DataContext.Models;
using DevHome.Identity.OpenIddict;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
    SignInManager<ApplicationUser> signInManager) : Controller
{
    /// <summary>
    /// Точка входа authorization code flow. Если пользователь не вошёл,
    /// перенаправляет на страницу входа Identity и возвращается сюда после неё.
    /// </summary>
    /// <remarks>
    /// Сценарий: DevHome открывает /connect/authorize → Identity проверяет вход →
    /// возвращает браузер в DevHome с одноразовым кодом → DevHome меняет код
    /// на токены в /connect/token. Код бесполезен без PKCE-verifier.
    /// </remarks>
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        // OpenIddict разбирает и проверяет запрос (client_id, redirect_uri, scope,
        // code_challenge) до вызова контроллера; здесь остаётся только результат.
        if (HttpContext.GetOpenIddictServerRequest() is not { } request)
        {
            return BadRequest("Не удалось прочитать OpenID Connect-запрос.");
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            // Challenge отправляет пользователя на страницу входа Identity,
            // сохраняя исходный адрес, чтобы вернуться к нему после входа.
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

        var principal = await CreatePrincipalAsync(user, request.GetScopes());

        // SignIn с серверной схемой OpenIddict: ответ формирует OpenIddict,
        // он же подписывает код авторизации/токен и делает редирект клиенту.
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Завершение сеанса: удаляет cookie Identity и завершает сеанс OpenIddict.
    /// </summary>
    /// <remarks>
    /// Сначала закрывается локальный сеанс Identity, затем OpenIddict завершает
    /// сеанс у клиента (front-channel/back-channel logout) и возвращает его
    /// на адрес из post_logout_redirect_uri.
    /// </remarks>
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await signInManager.SignOutAsync();

        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Страница подтверждения кода устройства. Пользователь должен быть аутентифицирован.
    /// </summary>
    /// <remarks>
    /// Device code flow: CLI получает код на /connect/deviceauthorization и показывает
    /// пользователю. Пользователь открывает эту страницу, вводит код и подтверждает вход.
    /// </remarks>
    [HttpGet("~/connect/verify")]
    public IActionResult Verify(string? user_code)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            // Код показывается уже после входа, иначе подтверждать было бы некому.
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        // Код подставляется в форму, чтобы пользователю не пришлось вводить его с устройства.
        ViewData["UserCode"] = user_code ?? string.Empty;
        return View();
    }

    /// <summary>
    /// Подтверждение кода устройства: OpenIddict связывает пользователя с ожидающим device code.
    /// </summary>
    /// <remarks>
    /// После этого ответа CLI при следующем опросе /connect/token получит токены.
    /// </remarks>
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

        // Пользователь может отказать: тогда CLI получит ошибку доступа
        // и не будет ждать токен до истечения кода устройства.
        if (!string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Подтверждение отклонено пользователем.");
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return BadRequest("Не удалось загрузить пользователя.");
        }

        var request = HttpContext.GetOpenIddictServerRequest();
        IEnumerable<string> scopes = request is null ? DevHomeScopes.User : request.GetScopes().ToArray();
        var principal = await CreatePrincipalAsync(user, scopes);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Собирает набор claim пользователя для выдачи токена.
    /// </summary>
    /// <remarks>
    /// Claim — это пара «тип-значение» внутри токена. Клиент читает их из
    /// identity-токена и из /connect/userinfo. Набор ограничен областью запроса:
    /// без области email адрес почты в токен не попадёт.
    /// </remarks>
    private async Task<ClaimsPrincipal> CreatePrincipalAsync(ApplicationUser user, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        // sub — постоянный идентификатор пользователя, name — отображаемое имя.
        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user));
        identity.SetClaim(Claims.Name, await userManager.GetUserNameAsync(user));

        var email = await userManager.GetEmailAsync(user);
        if (!string.IsNullOrEmpty(email))
        {
            identity.SetClaim(Claims.Email, email);
        }

        // Роли нужны клиенту для проверки прав без обращения к Identity.
        var roles = await userManager.GetRolesAsync(user);
        identity.SetClaims(Claims.Role, [.. roles]);

        var grantedScopes = scopes.ToList();
        identity.SetScopes(grantedScopes);

        // Ресурс devhome.api делает токен «адресованным» API DevHome:
        // без него API отклонит токен как выданный для другого ресурса.
        // devhome-api добавлен в аудиторию, чтобы клиент API мог проверять
        // этот токен через introspection.
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
    /// <remarks>
    /// <see cref="Destinations.IdentityToken"/> — токен идентификации: его читает клиент,
    /// <see cref="Destinations.AccessToken"/> — токен доступа: его читает API.
    /// Пользовательские данные кладутся в оба, чтобы клиенту не требовался лишний запрос.
    /// </remarks>
    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Subject or Claims.Name or Claims.Email or Claims.Role =>
            [Destinations.AccessToken, Destinations.IdentityToken],

        _ => [Destinations.AccessToken]
    };
}
