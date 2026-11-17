using System.Reflection;
using Common.Security.OpenIddict.Client.Web;
using Common.UI;
using DevHome.Client.Pages;
using DevHome.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Регистрируем сервисы приложения.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

// Меню навигации: страницы описывают себя атрибутом [MenuItem],
// а здесь задаются только общие настройки приложения.
builder.Services.AddCommonUi(options =>
{
    options.ApplicationName = "DevHome";
    options.ApplicationIcon = "bi bi-house-gear-fill";
    options.HomeUrl = "";
    options.LoginUrl = "login";
    options.LogoutUrl = "logout";
    //options.ProfileUrl = "https://localhost:7163/Account/Manage";
    // Вход и выход DevHome обслуживают обработчики /login и /logout.
    options.LogoutUsePost = false;
    options.AddAssembly(typeof(Program).Assembly);
    options.AddAssembly(typeof(DevHome.Client._Imports).Assembly);
});

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

// DevHome входит через DevHome.Identity так же, как обычные сервисы входят через Google.
// Настройка OIDC-клиента вынесена в библиотеку Common.Security.OpenIddict.Client.
var allowHttp = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue("Authentication:DevHome:AllowHttp", false);

var oidcOptions = OpenIddictWebClientExtensions.ReadDevHomeOptions(builder.Configuration, allowHttp);
builder.Services.AddOpenIddictWebClient(oidcOptions);

var app = builder.Build();

// В контейнере приложение слушает только http: HTTPS обычно снимает
// внешний reverse proxy. Настройка позволяет отключить перенаправление.
var useHttpsRedirection = app.Configuration.GetValue("Http:UseHttpsRedirection", true);

// Настраиваем конвейер обработки HTTP-запросов.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

    if (useHttpsRedirection)
    {
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

app.MapGet("/health", () =>
{
    var version = GetApplicationVersion();
    return Results.Ok(new { status = "ok", version });
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DevHome.Client._Imports).Assembly);

// Вход и выход через DevHome.Identity.
app.MapGet("/login", (string? returnUrl) => Results.Challenge(
    new AuthenticationProperties
    {
        RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
    },
    [OpenIdConnectDefaults.AuthenticationScheme]));

app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(
        OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" });
});

static string GetApplicationVersion()
{
    var assembly = typeof(Program).Assembly;

    return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}

app.Run();
