# Common.Security.OpenIddict.Server

Серверная библиотека для приложений, которые выступают провайдером OpenID Connect:
настройка OpenIddict, сертификаты, сценарий `client_credentials`, учёт устройств,
сеансов и журнал безопасности.

Библиотека не знает класса пользователя и модели конкретного приложения. Всё, что ей
нужно, — `DbContext` приложения, в котором уже хранятся Identity и OpenIddict.

Клиентские библиотеки: `../Common.Security.OpenIddict.Client` (WPF, MAUI, CLI) и
`../Common.Security.OpenIddict.Client.Web` (веб и API).

## Что входит

| Часть | Назначение |
|---|---|
| `OpenIddictServerExtensions` | регистрация OpenIddict, сертификатов, проверки токенов и сервисов безопасности |
| `OpenIddictServerOptions` | издатель, HTTP, время жизни токенов, сертификаты, дополнительные области |
| `OpenIddictCertificateLoader` | постоянные PFX-сертификаты для контейнера |
| `ClientCredentialsHandler` | выдача токена сервисному клиенту без пользователя |
| `OpenIddictServerDataSeeder` | создание и обновление клиентов и областей OpenIddict |
| `Security/UserDevice` | устройство, с которого входил пользователь |
| `Security/UserSession` | один вход пользователя |
| `Security/UserSessionApplication` | приложения, в которые входили в рамках сеанса |
| `Security/UserLoginAudit` | журнал входов, отзывов и блокировок |
| `Security/UserSecuritySetting` | политика одновременного входа |
| `IUserSecurityService` | сеансы, устройства, политика, отзыв, журнал |

## Требования

- .NET 11, ASP.NET Core, EF Core;
- PostgreSQL или другая СУБД, поддерживаемая EF Core;
- OpenIddict 7.8;
- `DbContext` приложения должен наследовать `IdentityDbContext` (или содержать таблицы Identity).

## Подключение

### 1. Ссылка на проект

```xml
<ProjectReference Include="..\Common.Security.OpenIddict.Server\Common.Security.OpenIddict.Server.csproj" />
```

При публикации в NuGet замените ссылку на пакет.

### 2. Контекст базы

Добавьте таблицы безопасности в `DbContext`:

```csharp
using Common.Security.OpenIddict.Server.Security;

public sealed class DBContext(DbContextOptions<DBContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserSessionApplication> SessionApplications => Set<UserSessionApplication>();
    public DbSet<UserLoginAudit> LoginAudit => Set<UserLoginAudit>();
    public DbSet<UserSecuritySetting> UserSecuritySettings => Set<UserSecuritySetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureOpenIddictSecurity();
    }
}
```

Создайте миграцию:

```powershell
dotnet ef migrations add AddDeviceSecurity `
  --project MyApp.Data `
  --startup-project MyApp.Web `
  --context DBContext
```

`DbContext` должен быть зарегистрирован в DI как scoped: библиотека использует его
напрямую (`UserSecurityService<TContext>`). Если приложение работает только через
`IDbContextFactory`, добавьте scoped-регистрацию:

```csharp
services.AddScoped<DBContext>(provider =>
    provider.GetRequiredService<IDbContextFactory<DBContext>>().CreateDbContext());
```

### 3. Регистрация в `Program.cs`

```csharp
using Common.Security.OpenIddict.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddOpenIddictSecurityServer<DBContext>(
    builder.Configuration,
    builder.Environment,
    options =>
    {
        // Область конкретного приложения. openid/profile/email/offline_access
        // библиотека добавляет сама.
        options.AdditionalScopes.Add("myapp.api");
    });
```

`AddOpenIddictSecurityServer` делает всё сразу:

- регистрирует `AddOpenIddict().AddCore(...).AddServer(...).AddValidation(...)`;
- подключает `UseEntityFrameworkCore().UseDbContext<DBContext>()`;
- настраивает конечные точки `connect/authorize`, `connect/token`, `connect/userinfo`,
  `connect/introspect`, `connect/deviceauthorization`, `connect/verify`, `connect/logout`;
- включает authorization code + PKCE, refresh token, device code и (по умолчанию)
  `client_credentials`;
- подключает постоянные сертификаты или ключи в памяти;
- регистрирует `IUserSecurityService`, `IDeviceIdentityService`, `ITokenRevocationService`;
- добавляет проверку сеанса в cookie Identity.

### 4. Настройки

`appsettings.json`:

```json
{
  "OpenIddict": {
    "Issuer": "http://192.168.0.10:5201/",
    "AllowHttp": true,
    "AccessTokenLifetime": "01:00:00",
    "RefreshTokenLifetime": "14.00:00:00",
    "DeviceCodeLifetime": "00:10:00",
    "EnableClientCredentialsFlow": true,
    "DisableAccessTokenEncryption": true,
    "Certificates": {
      "Directory": "App_Data/OpenIddict",
      "Password": "пароль-из-секрета"
    }
  },
  "Security": {
    "Sessions": {
      "DeviceCookieName": "MyApp.Device",
      "DeviceCookieLifetime": "730.00:00:00",
      "SessionLifetime": "30.00:00:00",
      "AuditRetention": "180.00:00:00",
      "TouchInterval": "00:05:00",
      "TrustForwardedHeaders": false
    }
  }
}
```

Если `Certificates:Directory` пуст, в Development используются временные ключи в памяти,
а вне Development — сертификаты разработчика. Для контейнера всегда задавайте каталог
на постоянном томе, иначе после перезапуска все refresh-токены станут недействительны.

### 5. Клиенты и области

Описания клиентов остаются в приложении: библиотека не знает, какие приложения
подключаются. Создание и обновление выполняет `OpenIddictServerDataSeeder`:

```csharp
var descriptor = new OpenIddictApplicationDescriptor
{
    ClientId = "myapp-web",
    ClientType = ClientTypes.Confidential,
    ClientSecret = "секрет",
    ConsentType = ConsentTypes.Implicit,
    DisplayName = "Моё приложение",
    RedirectUris = { new Uri("https://app.example/signin-oidc") },
    Permissions =
    {
        Permissions.Endpoints.Authorization,
        Permissions.Endpoints.Token,
        Permissions.GrantTypes.AuthorizationCode,
        Permissions.GrantTypes.RefreshToken,
        Permissions.ResponseTypes.Code,
        Permissions.Scopes.Profile,
        Permissions.Scopes.Email,
        Permissions.Prefixes.Scope + "myapp.api"
    }
};

await OpenIddictServerDataSeeder.EnsureApplicationAsync(
    applicationManager,
    descriptor,
    refresh: true);
```

Для области:

```csharp
await OpenIddictServerDataSeeder.EnsureScopeAsync(
    scopeManager,
    new OpenIddictScopeDescriptor
    {
        Name = "myapp.api",
        DisplayName = "MyApp API",
        Resources = { "myapp.api", "myapp-api" }
    },
    refresh: true);
```

## Как работает учёт устройств и сеансов

### Устройство

При первом входе библиотека создаёт случайное значение, кладёт его в cookie
`MyApp.Device` (по умолчанию `DevHome.Device`) и сохраняет в базе SHA-256-хеш.
Cookie:

- `HttpOnly` — недоступна JavaScript;
- `SameSite=Lax`;
- `Secure`, если запрос по HTTPS;
- живёт два года.

Пока cookie сохраняется, один и тот же браузер на одном компьютере считается одним
устройством. Очистка cookie или другой браузер создают новое устройство. Это ожидаемо:
без cookie отличить «тот же браузер» от нового нельзя.

### Сеанс

При успешном входе создаётся `UserSession` и в cookie Identity добавляется claim `sid`.
Тот же `sid` попадает в access- и identity-токены. `OnValidatePrincipal` проверяет
сеанс при каждом запросе: если он отозван, истёк или устройство заблокировано,
cookie отклоняется.

### Политики

| Политика | Поведение |
|---|---|
| `MultipleDevices` | вход с любого числа устройств |
| `SingleDeviceNewestWins` | новый вход отзывает все предыдущие сеансы |
| `SingleDeviceRequiresOldSessionEnd` | новый вход отклоняется, пока старый сеанс не завершён |

При смене политики на «одно устройство» самый свежий сеанс остаётся, остальные
завершаются сразу.

### Отзыв и блокировка

- `RevokeSessionAsync` помечает сеанс завершённым, отзывает связанные приложения
  и токены OpenIddict для этих клиентов.
- `RevokeAllSessionsAsync` завершает все сеансы и отзывает все токены пользователя.
- `BlockDeviceAsync` блокирует устройство, завершает его сеансы и запрещает будущие входы.
- `UnblockDeviceAsync` снимает запрет, но не восстанавливает старые сеансы.

После отзыва cookie Identity перестаёт приниматься на следующем запросе. API, которое
проверяет токены через introspection, получает `active: false` после отзыва токенов.
Клиентское приложение, которое уже держит свою cookie, может оставаться открытым до
следующего обращения к API или до выхода; принудительный back-channel logout в этой
версии не реализован.

## Журнал и приватность

`UserLoginAudit` хранит:

- пользователя, сеанс и устройство;
- ClientId и название приложения;
- IP-адрес и локацию;
- user agent;
- тип события (`SignIn`, `ApplicationAuthorized`, `SessionRevoked`, `DeviceBlocked` и т. д.);
- время и подробности.

IP берётся из `HttpContext.Connection.RemoteIpAddress`. `X-Forwarded-For` по умолчанию
не используется: клиент может подделать заголовок. Если приложение стоит за доверенным
прокси, включите `Security:Sessions:TrustForwardedHeaders=true`. Не включайте этот режим
при прямом доступе из локальной сети.

Геолокация по IP не выполняется: библиотека не обращается к внешним сервисам. Локация
берётся из заголовков доверенного прокси (`CF-IPCountry`, `X-Country-Code`, `X-Geo-City`)
или подписывается как «локальная сеть» / «этот компьютер». Это осознанно: домашний
сервер не должен отправлять IP пользователей третьим лицам.

## Проверка и обслуживание

- `GetActiveSessionsAsync`, `GetSessionHistoryAsync`, `GetDevicesAsync`, `GetAuditAsync` —
  данные для личного кабинета.
- `GetAllActiveSessionsAsync`, `GetAllAuditAsync` — данные для админки.
- `CleanupAuditAsync` удаляет записи старше `AuditRetention`. Вызывайте её по расписанию,
  например из `BackgroundService` раз в сутки.
- `DeleteUserDataAsync` удаляет устройства, сеансы и журнал при удалении учётной записи.

## Ограничения

- При первом запуске после обновления старые cookie Identity без claim `sid` будут
  отклонены: пользователи один раз войдут заново. Это ожидаемо.
- Библиотека рассчитана на один экземпляр приложения или общую базу: сеансы хранятся в БД.
- Для нескольких экземпляров нужны общие ключи Data Protection и общая БД.
- HTTP-режим (`AllowHttp`) допустим только в доверенной локальной сети.
- Для публичного размещения используйте HTTPS и сертификаты доверенного центра.
- Клиентские приложения не получают push-уведомление об отзыве; они узнают об этом
  при следующем обращении к API или Identity.
