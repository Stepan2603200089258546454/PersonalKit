using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Common.Security.OpenIddict.Server;

/// <summary>
/// Выдаёт access-токен по grant_type=client_credentials для сервисных клиентов.
/// </summary>
/// <remarks>
/// <para>
/// В OpenIddict 7 сценарий client_credentials не поддерживается стандартным
/// обработчиком: он рассчитан на выдачу токена пользователю. Здесь токен
/// выпускается от имени самого клиента-сервиса, без пользователя. Это нужно
/// для межсервисных вызовов: сервис получает токен и вызывает API.
/// </para>
/// <para>
/// Обработчик подключается в <see cref="OpenIddictServerExtensions"/> через
/// <c>AddEventHandler&lt;HandleTokenRequestContext&gt;</c> с типом
/// <see cref="OpenIddictServerHandlerType.Custom"/> и порядком
/// <c>int.MinValue + 100_000</c> — раньше стандартных обработчиков, но после
/// встроенных проверок запроса (валидация клиента и параметров).
/// </para>
/// </remarks>
public sealed class ClientCredentialsHandler(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager) : IOpenIddictServerHandler<HandleTokenRequestContext>
{
    /// <summary>Описание обработчика для регистрации в конвейере OpenIddict.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleTokenRequestContext>()
            .UseSingletonHandler<ClientCredentialsHandler>()
            .SetOrder(int.MinValue + 100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    /// <inheritdoc />
    public async ValueTask HandleAsync(HandleTokenRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.IsClientCredentialsGrantType())
        {
            return;
        }

        var application = await applicationManager.FindByClientIdAsync(context.Request.ClientId!);
        if (application is null)
        {
            context.Reject(
                error: Errors.InvalidClient,
                description: "Клиентское приложение не найдено.");

            return;
        }

        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await applicationManager.GetClientIdAsync(application));
        identity.SetClaim(Claims.Name, await applicationManager.GetDisplayNameAsync(application));
        identity.SetScopes(context.Request.GetScopes());
        identity.SetResources(await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

        identity.SetDestinations(static claim => claim.Type switch
        {
            Claims.Name or Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken]
        });

        context.SignIn(new ClaimsPrincipal(identity));
    }
}
