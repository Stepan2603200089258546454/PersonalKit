using System.Reflection;
using Common.UI;
using DevHome.Identity.Client.Pages;
using DevHome.Identity.Components;
using DevHome.Identity.Components.Account;
using DevHome.Identity.Data;
using DevHome.Identity.DataContext;
using DevHome.Identity.DataContext.Models;
using DevHome.Identity.OpenIddict;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Регистрируем сервисы приложения.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddControllersWithViews();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Меню навигации: страницы описывают себя атрибутом [MenuItem],
// а здесь задаются только общие настройки приложения.
builder.Services.AddCommonUi(options =>
{
    options.ApplicationName = "DevHome.Identity";
    options.ApplicationIcon = "bi bi-shield-lock-fill";
    options.HomeUrl = "";
    options.LoginUrl = "Account/Login";
    options.RegisterUrl = "Account/Register";
    options.ProfileUrl = "Account/Manage";
    options.LogoutUrl = "Account/Logout";
    options.AddAssembly(typeof(Program).Assembly);
    options.AddAssembly(typeof(DevHome.Identity.Client._Imports).Assembly);
});

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

RegistrationDataContext.AddIdentity(builder.Services, builder.Configuration);

// В песочницах и контейнерах каталог ключей Data Protection может быть недоступен,
// поэтому путь можно переопределить через конфигурацию. В Development ключи
// по умолчанию лежат рядом с приложением, а не в %LOCALAPPDATA%.
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionPath))
{
    dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
}

if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    Directory.CreateDirectory(dataProtectionPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}

// DevHome.Identity выступает OAuth 2.0/OpenID Connect-провайдером для остальных приложений DevHome.
builder.Services.AddDevHomeOpenIddict(builder.Configuration, builder.Environment);

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton<DatabaseReadiness>();
builder.Services.AddHostedService<DatabaseInitializer>();

var app = builder.Build();

// В контейнере приложение слушает только http: HTTPS обычно снимает
// внешний reverse proxy. Настройка позволяет отключить перенаправление.
var useHttpsRedirection = app.Configuration.GetValue("Http:UseHttpsRedirection", true);

// Настраиваем конвейер обработки HTTP-запросов.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

    if (useHttpsRedirection)
    {
        // Значение HSTS по умолчанию — 30 дней; для промышленной эксплуатации при необходимости увеличьте его: https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (useHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

// Проверка готовности для docker-compose: пока база не готова,
// контейнер не считается здоровым, и DevHome/API не запускаются.
app.MapGet("/health", (DatabaseReadiness readiness) =>
{
    var version = GetApplicationVersion();

    return readiness.IsReady
        ? Results.Ok(new { status = "ok", database = "ready", version })
        : Results.Json(
            new
            {
                status = "starting",
                database = "not-ready",
                version,
                error = readiness.LastError
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DevHome.Identity.Client._Imports).Assembly);

// Добавляем конечные точки, необходимые компонентам Identity из папки /Account.
app.MapAdditionalIdentityEndpoints();

static string GetApplicationVersion()
{
    var assembly = typeof(Program).Assembly;

    return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}

app.Run();
