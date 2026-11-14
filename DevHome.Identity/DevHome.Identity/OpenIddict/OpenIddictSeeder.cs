using DevHome.Identity.Administration;
using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
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

        await EnsureUsersAsync(scope.ServiceProvider, configuration, environment);
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

    /// <summary>
    /// Создаёт роли и демонстрационного пользователя, а также назначает администратора.
    /// </summary>
    /// <remarks>
    /// Администратором становится пользователь с почтой из <c>Identity:Admin:Email</c>.
    /// В Development, если ключ не задан, администратором назначается демо-пользователь.
    /// Так админка доступна сразу после первого запуска, а в рабочем окружении
    /// администратор задаётся явно в настройках или переменной окружения.
    /// </remarks>
    private static async Task EnsureUsersAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(OpenIddictSeeder));

        await EnsureRolesAsync(roleManager);

        var email = configuration["OpenIddict:DemoUser:Email"] ?? "demo@devhome.local";
        var demoPassword = configuration["OpenIddict:DemoUser:Password"] ?? "DevHome!2026";

        if (environment.IsDevelopment())
        {
            await EnsureDemoUserAsync(userManager, email, demoPassword, logger);
        }

        var adminEmail = configuration["Identity:Admin:Email"];
        if (string.IsNullOrWhiteSpace(adminEmail) && environment.IsDevelopment())
        {
            adminEmail = email;
        }

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogWarning(
                "Администратор не назначен: задайте Identity:Admin:Email в настройках. " +
                "Без него страницы /admin недоступны.");
            return;
        }

        var administrator = await userManager.FindByEmailAsync(adminEmail);
        if (administrator is null)
        {
            logger.LogInformation(
                "Пользователь {Email} из Identity:Admin:Email ещё не зарегистрирован: роль администратора будет назначена при следующем запуске.",
                adminEmail);
            return;
        }

        if (await userManager.IsInRoleAsync(administrator, AdminRoles.Administrator))
        {
            return;
        }

        var roleResult = await userManager.AddToRoleAsync(administrator, AdminRoles.Administrator);
        if (roleResult.Succeeded)
        {
            logger.LogInformation("Пользователь {Email} назначен администратором.", adminEmail);
        }
        else
        {
            logger.LogWarning(
                "Не удалось назначить администратора {Email}: {Errors}",
                adminEmail,
                string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }
    }

    private static async Task EnsureRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var roleName in new[] { AdminRoles.User, AdminRoles.Administrator })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole(roleName));
            }
        }
    }

    private static async Task EnsureDemoUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        ILogger logger)
    {
        var userName = email;

        // Демо-пользователь мог быть создан раньше с коротким именем: приводим имя к адресу почты.
        var user = await userManager.FindByNameAsync(userName) ?? await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                // Почта подтверждена сразу: демо-пользователь не проходит проверку письмом.
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "Не удалось создать демо-пользователя {Email}: {Errors}",
                    email,
                    string.Join("; ", result.Errors.Select(error => error.Description)));
                return;
            }
        }
        else if (!string.Equals(user.UserName, userName, StringComparison.Ordinal))
        {
            user.UserName = userName;
            await userManager.UpdateAsync(user);
        }

        if (!await userManager.IsInRoleAsync(user, AdminRoles.User))
        {
            await userManager.AddToRoleAsync(user, AdminRoles.User);
        }
    }
}
