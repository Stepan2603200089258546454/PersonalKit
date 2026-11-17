namespace Common.Security.OpenIddict.Server.Security;

/// <summary>
/// Имена claim, которые библиотека добавляет в cookie и токены.
/// </summary>
public static class OpenIddictSecurityClaimTypes
{
    /// <summary>
    /// Идентификатор сеанса. Стандартный claim OIDC <c>sid</c>: его понимают
    /// клиенты и можно использовать для back-channel logout.
    /// </summary>
    public const string SessionId = "sid";

    /// <summary>Причина, по которой вход был отклонён политикой безопасности.</summary>
    public const string SignInError = "devhome_security_error";
}
