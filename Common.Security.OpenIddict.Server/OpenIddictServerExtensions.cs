using Common.Security.OpenIddict.Server.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using HandleTokenRequestContext = global::OpenIddict.Server.OpenIddictServerEvents.HandleTokenRequestContext;

namespace Common.Security.OpenIddict.Server;

/// <summary>
/// Подключает сервер OpenID Connect и подсистему устройств/сеансов.
/// </summary>
/// <remarks>
/// <para>
/// Библиотека не знает ни класса пользователя, ни модели приложения. Всё, что ей
/// нужно, — <typeparamref name="TContext"/> с таблицами Identity и OpenIddict.
/// Поэтому её можно подключить к другому приложению, заменив только тип контекста
/// и описания клиентов.
/// </para>
/// <para>
/// После вызова добавьте в <c>OnModelCreating</c> вашего контекста
/// <c>modelBuilder.ConfigureOpenIddictSecurity()</c> и создайте миграцию.
/// </para>
/// </remarks>
public static class OpenIddictServerExtensions
{
    /// <summary>
    /// Регистрирует OpenIddict, сертификаты, проверку токенов и сервисы
    /// устройств/сеансов.
    /// </summary>
    /// <typeparam name="TContext">DbContext приложения, в котором хранятся Identity и OpenIddict.</typeparam>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <param name="environment">Окружение приложения.</param>
    /// <param name="configure">Дополнительная настройка сервера.</param>
    public static IServiceCollection AddOpenIddictSecurityServer<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<OpenIddictServerOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var serverOptions = ReadOptions(configuration);
        configure?.Invoke(serverOptions);

        services.AddHttpContextAccessor();
        services.Configure<UserSecurityOptions>(configuration.GetSection("Security:Sessions"));
        services.AddScoped<IUserSecurityService, UserSecurityService<TContext>>();
        services.AddScoped<IDeviceIdentityService, DeviceIdentityService<TContext>>();
        services.AddScoped<ITokenRevocationService, TokenRevocationService>();

        // Сеансы должны проверяться на каждом запросе к Identity: иначе отзыв
        // сеанса не завершит cookie до истечения её срока.
        services.AddOpenIddictSessionTracking();

        var certificates = OpenIddictCertificateLoader.LoadOrCreate(
            serverOptions,
            environment.ContentRootPath);

        services.AddOpenIddict()

            // Хранилище клиентов, областей, токенов и авторизаций — в основной
            // базе приложения. UseDbContext не создаёт отдельный контекст.
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<TContext>())

            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris("connect/authorize")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetIntrospectionEndpointUris("connect/introspect")
                    .SetDeviceAuthorizationEndpointUris("connect/deviceauthorization")
                    .SetEndUserVerificationEndpointUris("connect/verify");

                options
                    .AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow()
                    .AllowDeviceAuthorizationFlow();

                if (serverOptions.EnableClientCredentialsFlow)
                {
                    options.AllowClientCredentialsFlow();
                    options.AddEventHandler<HandleTokenRequestContext>(
                        builder => builder
                            .UseScopedHandler<ClientCredentialsHandler>()
                            .SetOrder(int.MinValue + 100_000));
                }

                options.RegisterScopes(BuildScopes(serverOptions));

                if (serverOptions.DisableAccessTokenEncryption)
                {
                    options.DisableAccessTokenEncryption();
                }

                options
                    .SetAccessTokenLifetime(serverOptions.AccessTokenLifetime)
                    .SetRefreshTokenLifetime(serverOptions.RefreshTokenLifetime)
                    .SetDeviceCodeLifetime(serverOptions.DeviceCodeLifetime);

                if (certificates is not null)
                {
                    options
                        .AddEncryptionCertificate(certificates.Encryption)
                        .AddSigningCertificate(certificates.Signing);
                }
                else if (environment.IsDevelopment())
                {
                    options
                        .AddEphemeralEncryptionKey()
                        .AddEphemeralSigningKey();
                }
                else
                {
                    options
                        .AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndUserVerificationEndpointPassthrough();

                if (environment.IsDevelopment() || serverOptions.AllowHttp)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }

                if (!string.IsNullOrWhiteSpace(serverOptions.Issuer))
                {
                    options.SetIssuer(new Uri(serverOptions.Issuer, UriKind.Absolute));
                }
            })

            // Локальная проверка токенов для /connect/userinfo и других
            // защищённых endpoint самой Identity. API проверяет токены
            // через introspection и эту библиотеку сервера не использует.
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }

    /// <summary>
    /// Подключает проверку сеанса к cookie Identity.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="scheme">Схема cookie. По умолчанию — схема Identity.</param>
    public static IServiceCollection AddOpenIddictSessionTracking(
        this IServiceCollection services,
        string? scheme = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        scheme ??= IdentityConstants.ApplicationScheme;
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        services.Configure<CookieAuthenticationOptions>(scheme, options =>
        {
            options.Events.OnSigningIn = OpenIddictSecurityCookieEvents.OnSigningInAsync;
            options.Events.OnValidatePrincipal = OpenIddictSecurityCookieEvents.OnValidatePrincipalAsync;
        });

        return services;
    }

    private static OpenIddictServerOptions ReadOptions(IConfiguration configuration)
    {
        var options = new OpenIddictServerOptions
        {
            Issuer = configuration["OpenIddict:Issuer"],
            AllowHttp = configuration.GetValue("OpenIddict:AllowHttp", false),
            CertificatesDirectory = configuration["OpenIddict:Certificates:Directory"],
            CertificatesPassword = configuration["OpenIddict:Certificates:Password"],
            EnableClientCredentialsFlow = configuration.GetValue("OpenIddict:EnableClientCredentialsFlow", true),
            DisableAccessTokenEncryption = configuration.GetValue("OpenIddict:DisableAccessTokenEncryption", true)
        };

        if (TimeSpan.TryParse(configuration["OpenIddict:AccessTokenLifetime"], out var accessTokenLifetime))
        {
            options.AccessTokenLifetime = accessTokenLifetime;
        }

        if (TimeSpan.TryParse(configuration["OpenIddict:RefreshTokenLifetime"], out var refreshTokenLifetime))
        {
            options.RefreshTokenLifetime = refreshTokenLifetime;
        }

        if (TimeSpan.TryParse(configuration["OpenIddict:DeviceCodeLifetime"], out var deviceCodeLifetime))
        {
            options.DeviceCodeLifetime = deviceCodeLifetime;
        }

        return options;
    }

    private static string[] BuildScopes(OpenIddictServerOptions options)
    {
        var scopes = new List<string>
        {
            Scopes.OpenId,
            Scopes.Profile,
            Scopes.Email,
            Scopes.OfflineAccess
        };

        foreach (var scope in options.AdditionalScopes)
        {
            if (!string.IsNullOrWhiteSpace(scope)
                && !scopes.Contains(scope, StringComparer.Ordinal))
            {
                scopes.Add(scope);
            }
        }

        return [.. scopes];
    }
}
