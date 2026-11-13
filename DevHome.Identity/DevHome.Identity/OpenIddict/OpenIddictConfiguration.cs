using DevHome.Identity.DataContext.Context;
using OpenIddict.Abstractions;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DevHome.Identity.OpenIddict;

/// <summary>
/// Регистрация DevHome.Identity в роли OAuth 2.0/OpenID Connect-провайдера.
/// </summary>
/// <remarks>
/// DevHome.Identity играет ту же роль, что Google или Microsoft для сторонних сервисов:
/// он показывает страницу входа, подтверждает пользователя и выдаёт клиентским приложениям
/// (DevHome, MAUI, WPF, CLI, сервисам) токены. Клиенты не получают доступ к базе Identity,
/// они работают только с токенами.
/// </remarks>
public static class OpenIddictConfiguration
{
    /// <summary>
    /// Подключает сервер OpenIddict, хранилище клиентов в PostgreSQL и проверку токенов.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <param name="configuration">Конфигурация приложения (секция <c>OpenIddict</c>).</param>
    /// <param name="environment">Окружение: в Development разрешены упрощения для localhost.</param>
    public static IServiceCollection AddDevHomeOpenIddict(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOpenIddict()

            // -----------------------------------------------------------------
            // Хранилище (Core). Клиенты, разрешения, области и токены хранятся
            // в основной базе Identity через EF Core, поэтому переживают
            // перезапуск приложения и видны всем экземплярам сервиса.
            // UseDbContext<DBContext> означает, что OpenIddict использует
            // зарегистрированный DBContext, а не создаёт собственный.
            // -----------------------------------------------------------------
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<DBContext>())

            // -----------------------------------------------------------------
            // Сервер (Server). Именно он выдаёт коды авторизации и токены.
            // -----------------------------------------------------------------
            .AddServer(options =>
            {
                // Адреса конечных точек. Значения относительные: итоговый адрес
                // складывается из базового адреса приложения, например
                // https://localhost:7163/connect/token.
                options
                    // Начало входа пользователя (authorization code flow).
                    .SetAuthorizationEndpointUris("connect/authorize")
                    // Завершение сеанса: выход из Identity и из клиента.
                    .SetEndSessionEndpointUris("connect/logout")
                    // Обмен кода/refresh-токена/кода устройства на токены.
                    .SetTokenEndpointUris("connect/token")
                    // Данные пользователя по access-токену (OIDC UserInfo).
                    .SetUserInfoEndpointUris("connect/userinfo")
                    // Device code flow: устройство получает код и опрашивает сервер.
                    .SetDeviceAuthorizationEndpointUris("connect/deviceauthorization")
                    // Страница, где пользователь вводит код устройства.
                    .SetEndUserVerificationEndpointUris("connect/verify");

                // Разрешённые сценарии (grant types). Каждый сценарий определяет,
                // каким способом клиент может получить токен.
                options
                    // Вход пользователя в веб-приложении (DevHome): браузер
                    // перенаправляется на Identity, после входа возвращается с кодом.
                    .AllowAuthorizationCodeFlow()
                    // PKCE обязателен для всех клиентов: перехваченный код
                    // бесполезен без verifier, который остаётся у клиента.
                    .RequireProofKeyForCodeExchange()
                    // Продление сеанса без повторного ввода пароля.
                    .AllowRefreshTokenFlow()
                    // Вход на устройствах без браузера (CLI, консоль):
                    // пользователь подтверждает код на телефоне или ПК.
                    .AllowDeviceAuthorizationFlow()
                    // Межсервисные вызовы без пользователя (devhome-service).
                    .AllowClientCredentialsFlow();

                // Области, которые сервер готов выдать клиентам.
                // Список включает openid, profile, email, offline_access и devhome.api.
                options.RegisterScopes(DevHomeScopes.User);

                // OpenIddict 7 не умеет client_credentials «из коробки»:
                // стандартный обработчик ожидает пользователя. Свой обработчик
                // выпускает токен от имени самого клиента-сервиса.
                options.AddEventHandler<HandleTokenRequestContext>(builder => builder
                    .UseScopedHandler<ClientCredentialsHandler>()
                    .SetOrder(int.MinValue + 100_000));

                // Access-токен остаётся JWT в открытом виде: клиенты и API
                // проверяют подпись сами, не обращаясь к Identity на каждый запрос.
                // Без этой строки токен был бы зашифрован и понятен только серверу.
                options.DisableAccessTokenEncryption();

                // Время жизни токенов.
                options
                    // Access-токен живёт час: компрометация ограничена по времени.
                    .SetAccessTokenLifetime(TimeSpan.FromHours(1))
                    // Refresh-токен живёт две недели, затем нужен повторный вход.
                    .SetRefreshTokenLifetime(TimeSpan.FromDays(14))
                    // Код устройства действует десять минут: пользователь успевает
                    // ввести его, а забытый код быстро становится недействительным.
                    .SetDeviceCodeLifetime(TimeSpan.FromMinutes(10));

                if (environment.IsDevelopment())
                {
                    // В разработке ключи подписи и шифрования создаются в памяти:
                    // не нужен сертификат в хранилище Windows и права на него.
                    // Минус: после перезапуска ранее выданные токены станут недействительны.
                    options
                        .AddEphemeralEncryptionKey()
                        .AddEphemeralSigningKey();
                }
                else
                {
                    // Вне Development берутся сертификаты разработчика: для рабочего
                    // окружения здесь должны быть постоянные сертификаты из хранилища,
                    // иначе токены перестанут проверяться после перезапуска и на
                    // нескольких экземплярах сервиса.
                    options
                        .AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }

                // Интеграция с ASP.NET Core. Passthrough означает, что OpenIddict
                // не отвечает сам, а передаёт запрос контроллеру приложения:
                // так работают страница входа, выход и форма кода устройства.
                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndUserVerificationEndpointPassthrough();

                if (environment.IsDevelopment())
                {
                    // В разработке разрешаем http, чтобы клиенты могли обращаться
                    // к localhost без сертификата. В рабочем окружении HTTPS обязателен.
                    aspNetCore.DisableTransportSecurityRequirement();
                }

                // Издатель (issuer) токенов. Если значение не задано, OpenIddict
                // подставляет адрес текущего запроса, поэтому localhost и имя хоста
                // в рабочем окружении дают разные issuer. Для стабильных токенов
                // issuer нужно задавать явно: OpenIddict:Issuer в настройках.
                var issuer = configuration["OpenIddict:Issuer"];
                if (!string.IsNullOrWhiteSpace(issuer))
                {
                    options.SetIssuer(new Uri(issuer, UriKind.Absolute));
                }
            })

            // -----------------------------------------------------------------
            // Проверка токенов (Validation) для защищённых конечных точек этого же
            // приложения, например /connect/userinfo. UseLocalServer — проверять
            // подпись локально, без сетевого обращения к самому себе.
            // -----------------------------------------------------------------
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }
}
