using System.Net.Http;
using System.Security.Claims;
using IdentityModel.OidcClient;

namespace Common.Security.OpenIddict.Client;

using IdentityModel.OidcClient.Browser;

/// <summary>
/// Настройки настольного или мобильного OIDC-клиента.
/// </summary>
public sealed class OpenIddictNativeClientOptions
{
    /// <summary>Адрес Identity.</summary>
    public string Authority { get; set; } = "http://127.0.0.1:5201/";

    /// <summary>ClientId зарегистрированного публичного клиента.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Адрес возврата, например <c>http://127.0.0.1:5181/callback</c>.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Области через пробел.</summary>
    public string Scope { get; set; } = "openid profile email offline_access devhome.api";

    /// <summary>Браузер, который откроет страницу входа. Реализуется платформой.</summary>
    public IBrowser? Browser { get; set; }

    /// <summary>Разрешить HTTP и самоподписанный сертификат локального Identity.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Принимать любой сертификат сервера. Только для локальной разработки.</summary>
    public bool AcceptAnyServerCertificate { get; set; }
}
