# Common.Security.OpenIddict.Client.Web

Клиентская библиотека для **веб-приложений ASP.NET Core** и **API**, которые работают
с OpenID Connect-провайдером (DevHome.Identity или другим совместимым сервером).

Для WPF, MAUI и CLI используйте `Common.Security.OpenIddict.Client`.

## Что входит

| Тип | Назначение |
|---|---|
| `OpenIddictClientOptions` | настройки веб-клиента |
| `AddOpenIddictWebClient` | cookie-аутентификация и OpenID Connect |
| `OpenIddictApiValidationOptions` / `AddOpenIddictApiValidation` | проверка access-токенов через introspection |

## Веб-приложение

```csharp
using Common.Security.OpenIddict.Client.Web;

var builder = WebApplication.CreateBuilder(args);

var allowHttp = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue("Authentication:MyApp:AllowHttp", false);

var options = new OpenIddictClientOptions
{
    Authority = builder.Configuration["Authentication:MyApp:Authority"]!,
    ClientId = builder.Configuration["Authentication:MyApp:ClientId"]!,
    ClientSecret = builder.Configuration["Authentication:MyApp:ClientSecret"],
    AllowHttp = allowHttp
};

options.Scopes.Add("myapp.api");

builder.Services.AddOpenIddictWebClient(options);
```

Эквивалент для DevHome (готовое чтение секции `Authentication:DevHome`):

```csharp
var options = OpenIddictWebClientExtensions.ReadDevHomeOptions(
    builder.Configuration,
    allowHttp);

builder.Services.AddOpenIddictWebClient(options);
```

Что настраивает библиотека:

- `DefaultScheme = Cookies`, `DefaultChallengeScheme = OpenIdConnect`;
- `ResponseType = code`, `UsePkce = true`;
- `SaveTokens`, `GetClaimsFromUserInfoEndpoint`;
- `CallbackPath` и `SignedOutCallbackPath`;
- `RequireHttpsMetadata`;
- при `AllowHttp`: отключает `Secure` у cookie, принимает самоподписанный сертификат,
  переключает `ResponseMode` на `query` и ослабляет `SameSite` cookie корреляции;
- сохраняет claim `sid` — идентификатор сеанса Identity.

### Вход и выход

```csharp
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
```

## API: проверка токена через introspection

```csharp
using Common.Security.OpenIddict.Client.Web;

builder.Services.AddOpenIddictApiValidation(new OpenIddictApiValidationOptions
{
    Issuer = builder.Configuration["OpenIddict:Issuer"]!,
    ClientId = builder.Configuration["OpenIddict:Validation:ClientId"]!,
    ClientSecret = builder.Configuration["OpenIddict:Validation:ClientSecret"]!
});
```

API не хранит ключи подписи и не читает базу Identity. Он отправляет токен в
`/connect/introspect` и получает `active: true/false`. После отзыва сеанса или токена
в Identity доступ к API закрывается.

## Что важно знать

- **Токены в веб-приложении** хранятся в cookie-сеансе ASP.NET Core (если `SaveTokens`).
  Для продакшена используйте HTTPS и не храните refresh-токены в открытом виде.
- **`sid`** — стандартный claim OIDC. Он связывает токен с сеансом Identity.
- **Отзыв сеанса** на сервере не удаляет cookie клиентского приложения мгновенно:
  приложение узнает об отзыве при следующем обращении к API или при проверке сеанса.
- **`AllowHttp`** нужен только для локальной сети и разработки. В интернете он
  отключает защиту транспорта.
- **Секрет клиента** не должен попадать в браузер. Для публичных клиентов секрет не нужен.

## Примеры в репозитории

- `DevHome/DevHome` — веб-клиент;
- `DevHome/DevHome.Api` — introspection.
