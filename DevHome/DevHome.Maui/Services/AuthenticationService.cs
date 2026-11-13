using System.Net.Http;
using System.Security.Claims;
using IdentityModel.OidcClient;

namespace DevHome.Maui.Services;

/// <summary>
/// Выполняет вход через DevHome.Identity и хранит профиль пользователя.
/// </summary>
public sealed class AuthenticationService
{
    private readonly OidcSettings _settings;
    private OidcClient? _client;

    public AuthenticationService(OidcSettings settings) => _settings = settings;

    public ClaimsPrincipal? User { get; private set; }

    public string? AccessToken { get; private set; }

    public DateTimeOffset? AccessTokenExpiration { get; private set; }

    public string? Error { get; private set; }

    public bool IsSignedIn => User is not null;

    public async Task<bool> LoginAsync()
    {
        Error = null;

        try
        {
            var result = await GetClient().LoginAsync(new LoginRequest());
            if (result.IsError)
            {
                Error = result.Error;
                return false;
            }

            User = result.User;
            AccessToken = result.AccessToken;
            AccessTokenExpiration = result.AccessTokenExpiration;

            return true;
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            return false;
        }
    }

    public void Logout()
    {
        User = null;
        AccessToken = null;
        AccessTokenExpiration = null;
        Error = null;
    }

    private OidcClient GetClient() => _client ??= new OidcClient(new OidcClientOptions
    {
        Authority = _settings.Authority,
        ClientId = _settings.ClientId,
        RedirectUri = _settings.RedirectUri,
        Scope = _settings.Scope,
        Browser = new SystemBrowser(),
#if DEBUG
        // В отладке разрешаем самоподписанный сертификат локального Identity.
        BackchannelHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        },
#endif
        Policy =
        {
            Discovery = { RequireHttps = false }
        }
    });
}
