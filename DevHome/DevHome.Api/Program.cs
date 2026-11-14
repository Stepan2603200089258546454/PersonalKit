using System.Reflection;
using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

// Шаблон API: публичные методы открыты, приватные проверяют access-токен
// через introspection в DevHome.Identity. API не читает базу Identity
// и не хранит ключи подписи, поэтому его можно запускать отдельно.
var builder = WebApplication.CreateBuilder(args);

var issuer = builder.Configuration["OpenIddict:Issuer"] ?? "http://localhost:5201/";
var clientId = builder.Configuration["OpenIddict:Validation:ClientId"] ?? "devhome-api";
var clientSecret = builder.Configuration["OpenIddict:Validation:ClientSecret"]
    ?? "devhome-api-development-secret";

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});

builder.Services.AddAuthorization(options =>
{
    // Роль попадает в access-токен из DevHome.Identity. Проверяем именно claim,
    // а не имя роли из cookie: API не использует cookie.
    options.AddPolicy("Admin", policy => policy.RequireClaim(Claims.Role, "Admin"));
});

builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        // Issuer должен совпадать с OpenIddict:Issuer в DevHome.Identity.
        options.SetIssuer(new Uri(issuer));

        // API проверяет токен не подписью, а запросом к /connect/introspect.
        // Клиент devhome-api создаётся сидером Identity и имеет только
        // разрешение на introspection.
        options.UseIntrospection()
            .SetClientId(clientId)
            .SetClientSecret(clientSecret);

        // Интеграция introspection с HttpClient: без неё OpenIddict
        // не знает, чем отправлять запрос в /connect/introspect.
        options.UseSystemNetHttp();

        options.UseAspNetCore();
    });

var app = builder.Build();

var useHttpsRedirection = app.Configuration.GetValue("Http:UseHttpsRedirection", true);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        exceptionHandlerApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "internal_server_error" });
        });
    });
}

if (useHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// Служебные и публичные методы: токен не нужен.
// ---------------------------------------------------------------------------

app.MapGet("/health", () =>
    Results.Ok(new { status = "ok", service = "DevHome.Api", version = GetApplicationVersion() }));

app.MapGet("/", () =>
    Results.Ok(new
    {
        service = "DevHome.Api",
        version = GetApplicationVersion(),
        documentation = "Публичные методы: /api/public/*. Приватные: /api/private/* (нужен Bearer-токен)."
    }));

app.MapGet("/api/public/ping", () =>
    Results.Ok(new
    {
        status = "ok",
        message = "Публичный метод доступен без токена.",
        utc = DateTimeOffset.UtcNow
    }));

app.MapGet("/api/public/info", (IConfiguration configuration) =>
    Results.Ok(new
    {
        service = "DevHome.Api",
        version = GetApplicationVersion(),
        authentication = new
        {
            type = "OpenID Connect / OAuth 2.0",
            validation = "introspection",
            issuer = configuration["OpenIddict:Issuer"] ?? "http://localhost:5201/",
            scope = "devhome.api"
        },
        endpoints = new
        {
            health = "/health",
            publicPing = "/api/public/ping",
            publicInfo = "/api/public/info",
            privateMe = "/api/private/me",
            privateData = "/api/private/data",
            privateAdmin = "/api/private/admin"
        }
    }));

// ---------------------------------------------------------------------------
// Приватные методы: нужен действующий access-токен с областью devhome.api.
// ---------------------------------------------------------------------------

app.MapGet("/api/private/me", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        message = "Токен действителен.",
        subject = user.FindFirst(Claims.Subject)?.Value,
        name = user.Identity?.Name ?? user.FindFirst(Claims.Name)?.Value,
        email = user.FindFirst(Claims.Email)?.Value,
        clientId = user.FindFirst(Claims.ClientId)?.Value,
        roles = user.FindAll(Claims.Role).Select(claim => claim.Value).Distinct().ToArray(),
        scopes = user.GetScopes().ToArray()
    }))
    .RequireAuthorization();

app.MapGet("/api/private/data", () =>
    Results.Ok(new
    {
        message = "Данные доступны только по действующему токену.",
        items = new[]
        {
            new { id = 1, name = "Пример записи 1" },
            new { id = 2, name = "Пример записи 2" }
        }
    }))
    .RequireAuthorization();

app.MapGet("/api/private/admin", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        message = "Метод доступен только роли Admin.",
        subject = user.FindFirst(Claims.Subject)?.Value,
        roles = user.FindAll(Claims.Role).Select(claim => claim.Value).Distinct().ToArray()
    }))
    .RequireAuthorization("Admin");

static string GetApplicationVersion()
{
    var assembly = typeof(Program).Assembly;

    return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}

app.Run();
