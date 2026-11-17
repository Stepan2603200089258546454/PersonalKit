using System.Security.Claims;
using Common.Security.OpenIddict.Client;

namespace DevHome.Maui.Services;

/// <summary>
/// Выполняет вход через DevHome.Identity и хранит профиль пользователя.
/// </summary>
/// <remarks>
/// Вся работа с OIDC вынесена в <see cref="OpenIddictNativeClient"/>.
/// </remarks>
public sealed class AuthenticationService
{
    private readonly OpenIddictNativeClient _client;

    public AuthenticationService(OidcSettings settings)
    {
        var allowHttp = settings.Authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

#if DEBUG
        // В отладке локальный Identity использует самоподписанный сертификат.
        const bool acceptAnyServerCertificate = true;
#else
        var acceptAnyServerCertificate = allowHttp;
#endif

        _client = new OpenIddictNativeClient(new OpenIddictNativeClientOptions
        {
            Authority = settings.Authority,
            ClientId = settings.ClientId,
            RedirectUri = settings.RedirectUri,
            Scope = settings.Scope,
            Browser = new SystemBrowser(),
            AllowHttp = allowHttp,
            AcceptAnyServerCertificate = acceptAnyServerCertificate
        });
    }

    public ClaimsPrincipal? User => _client.User;

    public string? AccessToken => _client.AccessToken;

    public DateTimeOffset? AccessTokenExpiration => _client.AccessTokenExpiration;

    public string? Error => _client.Error;

    public bool IsSignedIn => _client.IsSignedIn;

    public Task<bool> LoginAsync() => _client.LoginAsync();

    public void Logout() => _client.Logout();
}
