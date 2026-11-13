using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DevHome.Identity.OpenIddict;

/// <summary>
/// Создаёт области, клиентские приложения и демонстрационного пользователя при запуске.
/// </summary>
/// <remarks>
/// Данные хранятся в PostgreSQL, поэтому сиды выполняются один раз: при следующих
/// запусках клиенты уже найдены по ClientId и повторно не создаются.
/// </remarks>
public static class OpenIddictSeeder
{
    /// <summary>
    /// Инициализирует справочники OpenIddict и (в режиме разработки) тестовую учётную запись.
    /// </summary>
    /// <remarks>
    /// Вызывается фоновым сервисом <see cref="Data.DatabaseInitializer"/> после применения миграций.
    /// </remarks>
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        await using var scope = services.CreateAsyncScope();

        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        await EnsureScopeAsync(scopeManager);

        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await EnsureMauiClientAsync(applicationManager);
        await EnsureWpfClientAsync(applicationManager);
        await EnsureCliClientAsync(applicationManager);
        await EnsureWebClientAsync(applicationManager, configuration, environment);
        await EnsureServiceClientAsync(applicationManager, configuration, environment);

        if (environment.IsDevelopment())
        {
            await EnsureDemoUserAsync(scope.ServiceProvider, configuration);
        }
    }

    private static async Task EnsureScopeAsync(IOpenIddictScopeManager manager)
    {
        if (await manager.FindByNameAsync(DevHomeScopes.Api) is not null)
        {
            return;
        }

        await manager.CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = DevHomeScopes.Api,
            DisplayName = "DevHome API",
            Description = "Доступ к API приложений DevHome.",
            Resources = { DevHomeScopes.Api }
        });
    }

    private static Task EnsureMauiClientAsync(IOpenIddictApplicationManager manager) =>
        EnsurePublicClientAsync(
            manager,
            DevHomeClients.Maui,
            "DevHome — Blazor Hybrid (MAUI)",
            DevHomeClients.MauiRedirectUri);

    private static Task EnsureWpfClientAsync(IOpenIddictApplicationManager manager) =>
        EnsurePublicClientAsync(
            manager,
            DevHomeClients.Wpf,
            "DevHome — Blazor Hybrid (WPF)",
            DevHomeClients.WpfRedirectUri);

    private static async Task EnsurePublicClientAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        string displayName,
        Uri redirectUri)
    {
        if (await manager.FindByClientIdAsync(clientId) is not null)
        {
            return;
        }

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            // Публичный клиент: настольное приложение не может хранить секрет,
            // поэтому защита строится на PKCE и точном адресе возврата.
            ClientType = ClientTypes.Public,
            // Согласие не спрашивается: приложение своё, экран согласия не нужен.
            ConsentType = ConsentTypes.Implicit,
            DisplayName = displayName,
            // Куда браузер вернётся с кодом авторизации и после выхода.
            RedirectUris = { redirectUri },
            PostLogoutRedirectUris = { redirectUri },
            // Разрешения клиента: какие конечные точки и сценарии ему доступны.
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Prefixes.Scope + DevHomeScopes.Api
            },
            // Требование PKCE: без verifier обмен кода на токен невозможен.
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange
            }
        });
    }

    private static async Task EnsureCliClientAsync(IOpenIddictApplicationManager manager)
    {
        if (await manager.FindByClientIdAsync(DevHomeClients.Cli) is not null)
        {
            return;
        }

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = DevHomeClients.Cli,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "DevHome — командная строка",
            // У консольного клиента нет браузера и адреса возврата: только device code flow.
            Permissions =
            {
                Permissions.Endpoints.DeviceAuthorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.DeviceCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Prefixes.Scope + DevHomeScopes.Api
            }
        });
    }

    private static async Task EnsureWebClientAsync(
        IOpenIddictApplicationManager manager,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var redirectUri = new Uri(configuration["OpenIddict:Clients:Web:RedirectUri"]
            ?? "https://localhost:7270/signin-oidc");

        if (await manager.FindByClientIdAsync(DevHomeClients.Web) is { } existing)
        {
            if (!environment.IsDevelopment())
            {
                return;
            }

            // В разработке пересоздаём клиента, чтобы применить новые адреса возврата.
            await manager.DeleteAsync(existing);
        }

        var secret = configuration["OpenIddict:Clients:Web:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Не задан секрет клиента DevHome: OpenIddict:Clients:Web:Secret.");
            }

            secret = "devhome-web-development-secret";
        }

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = DevHomeClients.Web,
            // Конфиденциальный клиент: серверный код DevHome может хранить секрет,
            // поэтому дополнительно к PKCE проверяется client_secret.
            ClientType = ClientTypes.Confidential,
            ClientSecret = secret,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "DevHome — веб-приложение",
            // Перечислены оба варианта адреса приложения: https для отладки
            // по сертификату разработчика и http для запуска без сертификата.
            RedirectUris =
            {
                redirectUri,
                new Uri("http://localhost:5149/signin-oidc"),
                new Uri("http://127.0.0.1:5149/signin-oidc")
            },
            PostLogoutRedirectUris =
            {
                new Uri(redirectUri, "/signout-callback-oidc"),
                new Uri("http://localhost:5149/signout-callback-oidc"),
                new Uri("http://127.0.0.1:5149/signout-callback-oidc")
            },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Prefixes.Scope + DevHomeScopes.Api
            }
        });
    }

    private static async Task EnsureServiceClientAsync(
        IOpenIddictApplicationManager manager,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (await manager.FindByClientIdAsync(DevHomeClients.Service) is not null)
        {
            return;
        }

        var secret = configuration["OpenIddict:Clients:Service:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Не задан секрет клиента DevHome: OpenIddict:Clients:Service:Secret.");
            }

            secret = "devhome-service-development-secret";
        }

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = DevHomeClients.Service,
            ClientType = ClientTypes.Confidential,
            ClientSecret = secret,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "DevHome — сервис",
            // Сервисный клиент ходит только в token endpoint и только за
            // токеном client_credentials: ни входа пользователя, ни refresh-токена.
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + DevHomeScopes.Api
            }
        });
    }

    private static async Task EnsureDemoUserAsync(IServiceProvider services, IConfiguration configuration)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        const string roleName = "User";
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new ApplicationRole(roleName));
        }

        var email = configuration["OpenIddict:DemoUser:Email"] ?? "demo@devhome.local";
        var password = configuration["OpenIddict:DemoUser:Password"] ?? "DevHome!2026";
        var userName = email;

        if (await userManager.FindByNameAsync(userName) is not null)
        {
            return;
        }

        // Демо-пользователь мог быть создан раньше с коротким именем: приводим имя к адресу почты.
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            existing.UserName = userName;
            await userManager.UpdateAsync(existing);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            // Почта подтверждена сразу: демо-пользователь не проходит проверку письмом.
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, roleName);
        }
    }
}
