namespace Common.Security.OpenIddict.Client.Web;

/// <summary>
/// Настройки веб-клиента OpenID Connect.
/// </summary>
/// <remarks>
/// Библиотека не привязана к DevHome: приложение передаёт адрес Identity,
/// ClientId, секрет и список областей. Значения по умолчанию соответствуют
/// локальному запуску Identity.
/// </remarks>
public sealed class OpenIddictClientOptions
{
    /// <summary>Адрес Identity, например <c>https://localhost:7163/</c>.</summary>
    public string Authority { get; set; } = "https://localhost:7163/";

    /// <summary>ClientId приложения, зарегистрированного в Identity.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Секрет конфиденциального клиента. Для публичного клиента пусто.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Области, которые запрашивает приложение.</summary>
    public IList<string> Scopes { get; } = new List<string>
    {
        "openid",
        "profile",
        "email",
        "offline_access"
    };

    /// <summary>Путь обработки входа. Должен совпадать с адресом возврата в Identity.</summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>Путь обработки выхода.</summary>
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

    /// <summary>Разрешить HTTP и самоподписанный сертификат. Только локальная сеть.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Адрес метаданных, если он отличается от <see cref="Authority"/>.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Сохранять access- и refresh-токены в cookie-сеансе приложения.</summary>
    public bool SaveTokens { get; set; } = true;

    /// <summary>Запрашивать профиль через UserInfo, а не только из identity-токена.</summary>
    public bool GetClaimsFromUserInfoEndpoint { get; set; } = true;
}
