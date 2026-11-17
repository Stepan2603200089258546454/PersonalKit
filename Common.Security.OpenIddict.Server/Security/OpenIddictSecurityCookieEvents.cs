using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// События cookie Identity, которые связывают cookie с записью сеанса.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OnSigningInAsync"/> вызывается перед записью cookie. Библиотека
/// определяет устройство, создаёт сеанс и добавляет в cookie claim <c>sid</c>.
/// <see cref="OnValidatePrincipalAsync"/> вызывается при каждом запросе: если
/// сеанс отозван, устройство заблокировано или истекло, cookie отклоняется.
/// </para>
/// <para>
/// Не заменяйте <c>OnValidatePrincipal</c> своим обработчиком без вызова
/// <see cref="SecurityStampValidator.ValidatePrincipalAsync"/>: иначе перестанет
/// работать смена пароля и принудительный выход через security stamp.
/// </para>
/// </remarks>
public static class OpenIddictSecurityCookieEvents
{
    /// <summary>Создаёт сеанс и добавляет <c>sid</c> в cookie при входе.</summary>
    public static async Task OnSigningInAsync(CookieSigningInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (principal.HasClaim(claim => claim.Type == OpenIddictSecurityClaimTypes.SessionId))
        {
            return;
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        // RefreshSignInAsync (например, после смены пароля) создаёт новый principal
        // без sid. Сохраняем текущий сеанс, если он ещё действует.
        var existingSessionId = context.HttpContext.User?.FindFirstValue(OpenIddictSecurityClaimTypes.SessionId);
        if (Guid.TryParse(existingSessionId, out var existingId))
        {
            var existingUserId = context.HttpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(existingUserId, userId, StringComparison.Ordinal))
            {
                var securityService = context.HttpContext.RequestServices.GetRequiredService<IUserSecurityService>();
                var validation = await securityService.ValidateSessionAsync(userId, existingId);
                if (validation.IsActive)
                {
                    AddSessionClaim(principal, existingId);
                    return;
                }
            }
        }

        var deviceService = context.HttpContext.RequestServices.GetRequiredService<IDeviceIdentityService>();
        var resolution = await deviceService.ResolveAsync(userId, context.HttpContext);

        if (resolution.IsBlocked)
        {
            principal.Identities.First().AddClaim(new Claim(
                OpenIddictSecurityClaimTypes.SignInError,
                "Устройство заблокировано. Вход с него запрещён."));

            return;
        }

        var options = context.HttpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<UserSecurityOptions>>()
            .Value;

        var userSecurity = context.HttpContext.RequestServices.GetRequiredService<IUserSecurityService>();
        var request = RequestContextInfo.FromHttpContext(context.HttpContext, options.TrustForwardedHeaders);
        var authenticationMethod = principal.FindFirst("amr")?.Value
            ?? principal.FindFirst(ClaimTypes.AuthenticationMethod)?.Value;

        SessionStartResult result;
        try
        {
            result = await userSecurity.StartSessionAsync(
                userId,
                resolution.Device.Id,
                options.IdentityClientId,
                options.IdentityApplicationName,
                request,
                authenticationMethod);
        }
        catch (Exception exception)
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(OpenIddictSecurityCookieEvents));

            logger.LogError(exception, "Не удалось создать сеанс пользователя {UserId}.", userId);
            principal.Identities.First().AddClaim(new Claim(
                OpenIddictSecurityClaimTypes.SignInError,
                "Не удалось создать сеанс. Попробуйте войти ещё раз."));
            return;
        }

        if (result.SessionId is { } sessionId)
        {
            AddSessionClaim(principal, sessionId);
            return;
        }

        principal.Identities.First().AddClaim(new Claim(
            OpenIddictSecurityClaimTypes.SignInError,
            result.Message ?? "Не удалось начать сеанс."));
    }

    /// <summary>Проверяет сеанс при каждом запросе и продлевает его активность.</summary>
    public static async Task OnValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Сначала стандартная проверка Identity: смена пароля, security stamp,
        // блокировка учётной записи. Без неё наши проверки были бы неполными.
        await SecurityStampValidator.ValidatePrincipalAsync(context);

        if (context.Principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Principal.FindFirstValue("sub");
        var sessionId = context.Principal.FindFirstValue(OpenIddictSecurityClaimTypes.SessionId);

        if (string.IsNullOrWhiteSpace(userId)
            || !Guid.TryParse(sessionId, out var parsedSessionId))
        {
            context.RejectPrincipal();
            return;
        }

        if (context.Principal.HasClaim(claim => claim.Type == OpenIddictSecurityClaimTypes.SignInError))
        {
            context.RejectPrincipal();
            return;
        }

        var securityService = context.HttpContext.RequestServices.GetRequiredService<IUserSecurityService>();
        var validation = await securityService.ValidateSessionAsync(userId, parsedSessionId);
        if (!validation.IsActive)
        {
            context.RejectPrincipal();
            return;
        }

        try
        {
            await securityService.TouchSessionAsync(userId, parsedSessionId);
        }
        catch (Exception exception)
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(OpenIddictSecurityCookieEvents));

            logger.LogWarning(
                exception,
                "Не удалось обновить время активности сеанса {SessionId}.",
                parsedSessionId);
        }
    }

    private static void AddSessionClaim(ClaimsPrincipal principal, Guid sessionId)
    {
        var identity = principal.Identities.FirstOrDefault();
        identity?.AddClaim(new Claim(OpenIddictSecurityClaimTypes.SessionId, sessionId.ToString()));
    }
}
