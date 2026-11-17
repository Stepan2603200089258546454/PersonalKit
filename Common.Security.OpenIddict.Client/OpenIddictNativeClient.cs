using System.Net.Http;
using System.Security.Claims;
using IdentityModel.OidcClient;

namespace Common.Security.OpenIddict.Client;

/// <summary>
/// Выполняет вход через Identity в настольном или мобильном приложении.
/// </summary>
/// <remarks>
/// Обёртка над <see cref="OidcClient"/> из IdentityModel.OidcClient: библиотека
/// задаёт общие правила (PKCE, HTTP для локальной сети, обработка ошибок),
/// а приложение передаёт платформенный браузер. Токены хранятся в памяти;
/// для долговременного хранения приложение может сохранить refresh-токен само.
/// </remarks>
public sealed class OpenIddictNativeClient
{
    private readonly OidcClient _client;

    /// <summary>Создаёт клиент по настройкам приложения.</summary>
    public OpenIddictNativeClient(OpenIddictNativeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RedirectUri);

        var oidcOptions = new OidcClientOptions
        {
            Authority = options.Authority,
            ClientId = options.ClientId,
            RedirectUri = options.RedirectUri,
            Scope = options.Scope,
            Browser = options.Browser,
            Policy =
            {
                Discovery = { RequireHttps = !options.AllowHttp }
            }
        };

        if (options.AcceptAnyServerCertificate || options.AllowHttp)
        {
            oidcOptions.BackchannelHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
        }

        _client = new OidcClient(oidcOptions);
    }

    /// <summary>Профиль вошедшего пользователя.</summary>
    public ClaimsPrincipal? User { get; private set; }

    /// <summary>Access-токен для вызовов API.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>Refresh-токен, если Identity его выдал.</summary>
    public string? RefreshToken { get; private set; }

    /// <summary>Срок действия access-токена.</summary>
    public DateTimeOffset? AccessTokenExpiration { get; private set; }

    /// <summary>Последняя ошибка входа.</summary>
    public string? Error { get; private set; }

    /// <summary>Пользователь вошёл.</summary>
    public bool IsSignedIn => User is not null;

    /// <summary>Открывает браузер и выполняет вход.</summary>
    public async Task<bool> LoginAsync(CancellationToken cancellationToken = default)
    {
        Error = null;

        try
        {
            var result = await _client.LoginAsync(new LoginRequest(), cancellationToken);
            if (result.IsError)
            {
                Error = result.Error;
                return false;
            }

            User = result.User;
            AccessToken = result.AccessToken;
            RefreshToken = result.RefreshToken;
            AccessTokenExpiration = result.AccessTokenExpiration;
            return true;
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            return false;
        }
    }

    /// <summary>Забывает локальные токены. Серверный сеанс завершается отдельно.</summary>
    public void Logout()
    {
        User = null;
        AccessToken = null;
        RefreshToken = null;
        AccessTokenExpiration = null;
        Error = null;
    }
}
