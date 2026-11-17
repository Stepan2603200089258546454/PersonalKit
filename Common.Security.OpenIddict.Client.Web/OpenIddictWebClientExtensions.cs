using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Common.Security.OpenIddict.Client.Web;

/// <summary>
/// Подключает веб-приложение к Identity по OpenID Connect.
/// </summary>
/// <remarks>
/// Используется authorization code flow с PKCE. Cookie приложения хранит сеанс
/// браузера, а Identity выдаёт identity- и access-токены. В Docker приложение
/// может работать по HTTP: тогда <see cref="OpenIddictClientOptions.AllowHttp"/>
/// отключает требование HTTPS и проверку самоподписанного сертификата.
/// </remarks>
public static class OpenIddictWebClientExtensions
{
    /// <summary>Добавляет cookie-аутентификацию и OpenID Connect-клиент.</summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="options">Настройки клиента.</param>
    public static IServiceCollection AddOpenIddictWebClient(
        this IServiceCollection services,
        OpenIddictClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);

        var allowHttp = options.AllowHttp;

        services.AddAuthentication(authentication =>
            {
                authentication.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                authentication.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                if (allowHttp)
                {
                    cookie.Cookie.SecurePolicy = CookieSecurePolicy.None;
                }
            })
            .AddOpenIdConnect(oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;

                if (!string.IsNullOrWhiteSpace(options.ClientSecret))
                {
                    oidc.ClientSecret = options.ClientSecret;
                }

                oidc.ResponseType = "code";
                oidc.UsePkce = true;
                oidc.SaveTokens = options.SaveTokens;
                oidc.GetClaimsFromUserInfoEndpoint = options.GetClaimsFromUserInfoEndpoint;
                oidc.CallbackPath = options.CallbackPath;
                oidc.SignedOutCallbackPath = options.SignedOutCallbackPath;
                oidc.RequireHttpsMetadata = !allowHttp;
                oidc.TokenValidationParameters.NameClaimType = "name";

                // sid нужен приложению, чтобы связать локальный cookie-сеанс
                // с записью сеанса Identity. Сохраняем claim без переименования.
                oidc.ClaimActions.MapUniqueJsonKey("sid", "sid");

                if (!string.IsNullOrWhiteSpace(options.MetadataAddress))
                {
                    oidc.MetadataAddress = options.MetadataAddress;
                }

                oidc.Scope.Clear();
                foreach (var scope in options.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
                {
                    oidc.Scope.Add(scope);
                }

                if (!allowHttp)
                {
                    return;
                }

                // Локальный Identity использует самоподписанный сертификат.
                // Для домашней сети это допустимо; в интернете так делать нельзя.
                oidc.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.None;
                oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.None;

                // При HTTP SameSite=None требует Secure, поэтому cookie корреляции
                // теряется. Возвращаем код в query и ослабляем SameSite.
                oidc.ResponseMode = "query";
                oidc.CorrelationCookie.SameSite = SameSiteMode.Unspecified;
                oidc.NonceCookie.SameSite = SameSiteMode.Unspecified;
            });

        return services;
    }

    /// <summary>Создаёт настройки из секции конфигурации <c>Authentication:DevHome</c>.</summary>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="allowHttp">Разрешён ли HTTP.</param>
    public static OpenIddictClientOptions ReadDevHomeOptions(
        IConfiguration configuration,
        bool allowHttp)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new OpenIddictClientOptions
        {
            Authority = configuration["Authentication:DevHome:Authority"] ?? "https://localhost:7163/",
            ClientId = configuration["Authentication:DevHome:ClientId"] ?? "devhome-web",
            ClientSecret = configuration["Authentication:DevHome:ClientSecret"] ?? "devhome-web-development-secret",
            AllowHttp = allowHttp,
            MetadataAddress = configuration["Authentication:DevHome:MetadataAddress"]
        };

        var scopes = configuration["Authentication:DevHome:Scopes"];
        if (!string.IsNullOrWhiteSpace(scopes))
        {
            options.Scopes.Clear();
            foreach (var scope in scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.Scopes.Add(scope);
            }
        }
        else
        {
            options.Scopes.Add("devhome.api");
        }

        return options;
    }
}
