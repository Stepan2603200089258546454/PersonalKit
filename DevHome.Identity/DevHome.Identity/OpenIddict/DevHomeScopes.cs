using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DevHome.Identity.OpenIddict;

/// <summary>
/// Области (scopes), которые выдаёт DevHome.Identity.
/// </summary>
/// <remarks>
/// Область — это «разрешение на часть данных». Клиент указывает области в запросе,
/// сервер проверяет, что они разрешены клиенту, и кладёт их в токен.
/// </remarks>
internal static class DevHomeScopes
{
    /// <summary>
    /// Идентификатор API-области DevHome: право вызывать API приложений DevHome.
    /// Именно эта область проверяется в access-токене на стороне API.
    /// </summary>
    public const string Api = "devhome.api";

    /// <summary>
    /// Области, запрашиваемые клиентскими приложениями с входом пользователя.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="Scopes.OpenId"/> — обязательная область OIDC: выдаёт идентификатор пользователя (sub).</item>
    /// <item><see cref="Scopes.Profile"/> — имя и прочие данные профиля (name, preferred_username).</item>
    /// <item><see cref="Scopes.Email"/> — адрес электронной почты (email, email_verified).</item>
    /// <item><see cref="Scopes.OfflineAccess"/> — право получить refresh-токен,
    ///       то есть продлевать сеанс без повторного входа.</item>
    /// <item><see cref="Api"/> — право обращаться к API DevHome.</item>
    /// </list>
    /// Роли пользователя отдельной областью не выдаются: они попадают в токен,
    /// потому что <see cref="Controllers.AuthorizationController"/> кладёт claim роли
    /// в access-токен и в identity-токен.
    /// </remarks>
    public static readonly string[] User =
    [
        Scopes.OpenId,
        Scopes.Profile,
        Scopes.Email,
        Scopes.OfflineAccess,
        Api
    ];
}
