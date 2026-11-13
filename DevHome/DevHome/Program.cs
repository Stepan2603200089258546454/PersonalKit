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
var authority = builder.Configuration["Authentication:DevHome:Authority"] ?? "https://localhost:7163/";

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Локальный запуск по http: cookie без флага Secure.
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = authority;
        options.ClientId = builder.Configuration["Authentication:DevHome:ClientId"] ?? "devhome-web";
        options.ClientSecret = builder.Configuration["Authentication:DevHome:ClientSecret"] ?? "devhome-web-development-secret";
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters.NameClaimType = "name";

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("offline_access");
        options.Scope.Add("devhome.api");

        if (builder.Environment.IsDevelopment())
        {
            // В разработке DevHome.Identity использует самоподписанный сертификат,
            // а приложение может запускаться по http.
            options.BackchannelHttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.None;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.None;

            // При локальном запуске по http используем redirect c кодом в query,
            // иначе SameSite=None требует Secure и cookie корреляции теряется.
            options.ResponseMode = "query";
            options.CorrelationCookie.SameSite = SameSiteMode.Unspecified;
            options.NonceCookie.SameSite = SameSiteMode.Unspecified;
        }
    });

var app = builder.Build();

// Настраиваем конвейер обработки HTTP-запросов.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

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

app.Run();
