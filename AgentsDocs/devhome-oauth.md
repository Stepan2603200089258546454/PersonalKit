# Вход в DevHome через DevHome.Identity

`DevHome.Identity` работает как обычный OAuth 2.0/OpenID Connect-провайдер: так же, как Google выдаёт токены сторонним сервисам, DevHome.Identity выдаёт токены приложениям DevHome. Вход пользователя выполняется на страницах Identity, а клиент получает access-токен и профиль.

Разбор настроек OpenIddict «что за что отвечает»: [openiddict-reference.md](openiddict-reference.md).
Клиентская библиотека для настольных приложений и CLI: `../Common.Security.OpenIddict.Client/README.md`.
Веб-клиент и API: `../Common.Security.OpenIddict.Client.Web/README.md`.
Устройства и сеансы: [security-sessions.md](security-sessions.md).

## Что поддерживается

| Сценарий | Поток | Клиент |
|---|---|---|
| Веб-приложение DevHome | authorization code + PKCE | `devhome-web` (конфиденциальный, с секретом) |
| Blazor Hybrid (MAUI) | authorization code + PKCE, loopback | `devhome-maui` |
| Blazor Hybrid (WPF) | authorization code + PKCE, loopback | `devhome-wpf` |
| Командная строка | device code flow | `devhome-cli` |
| Сервис-сервис | client credentials | `devhome-service` |

Конечные точки (по умолчанию `https://localhost:7163`):

- `/.well-known/openid-configuration` — метаданные провайдера;
- `/connect/authorize` — вход и выдача кода;
- `/connect/token` — обмен кода/device code/refresh token на токены;
- `/connect/deviceauthorization` — запрос кода устройства;
- `/connect/verify` — подтверждение кода устройства пользователем;
- `/connect/userinfo` — профиль по access-токену;
- `/connect/introspect` — проверка токена для API;
- `/connect/logout` — завершение сеанса.

Области: `openid`, `profile`, `email`, `offline_access`, `devhome.api`.

## Запуск

1. PostgreSQL должен быть доступен по строке из `DevHome.Identity/appsettings.json` (`DBConfiguration:DefaultConnection`).
2. Запустите Identity (профиль `https`, порт 7163):

   ```powershell
   dotnet run --project DevHome.Identity/DevHome.Identity
   ```

   При старте применяются миграции (`InitialCreate`, `AddDeviceSecurity`), создаются области, клиенты и (в Development) демо-учётная запись.

3. Демо-учётная запись: `demo@devhome.local` / `DevHome!2026`.

При первом запуске в Development используются временные ключи OpenIddict: после перезапуска выданные токены становятся недействительными. В промышленной среде настройте постоянные сертификаты подписи и шифрования, задайте `OpenIddict:Issuer` и уберите `DisableTransportSecurityRequirement`.

## Клиенты

Все клиенты уже зарегистрированы сидером (`OpenIddictSeeder`), повторный запуск не создаёт дубликатов. В Development и при `OpenIddict:Seed:RefreshClients=true` веб-клиент обновляется, чтобы применить адреса возврата. Создание и обновление выполняет `OpenIddictServerDataSeeder` из серверной библиотеки; описания клиентов остаются в приложении.

| Клиент | Redirect URI | Секрет |
|---|---|---|
| `devhome-maui` | `http://127.0.0.1:5180/callback` | нет (public) |
| `devhome-wpf` | `http://127.0.0.1:5181/callback` | нет (public) |
| `devhome-web` | `https://localhost:7270/signin-oidc` | `OpenIddict:Clients:Web:Secret` |
| `devhome-cli` | — (device code) | нет |
| `devhome-api` | — (introspection) | `OpenIddict:Clients:Api:Secret` |

Сервисный клиент `devhome-service` использует секрет `OpenIddict:Clients:Service:Secret`. Клиент `devhome-api` нужен только для introspection в шаблонном API. В Development подставляются значения по умолчанию из `appsettings.json`; в промышленной среде задавайте секреты через user-secrets или переменные окружения.

Запуск в Docker с локальной сетью описан в [docker.md](docker.md).

## Примеры

- `DevHome` — веб-приложение, входит через OpenID Connect, показывает профиль и роли.
- `DevHome.Wpf` — Blazor Hybrid на WPF, системный браузер + loopback-редирект.
- `DevHome.Maui` — Blazor Hybrid на .NET MAUI (Windows), тот же сценарий.
- `DevHome.Cli` — консоль: выводит код и ссылку, опрашивает токен-эндпоинт, показывает профиль и обновляет токен.

CLI:

```powershell
dotnet run --project DevHome/DevHome.Cli
```

WPF:

```powershell
dotnet run --project DevHome/DevHome.Wpf
```

MAUI (Windows):

```powershell
dotnet build DevHome/DevHome.Maui -f net11.0-windows10.0.19041.0
```

Клиенты читают адрес провайдера из `appsettings.json` (`Authentication:DevHome:Authority`), MAUI — из переменной `DEVHOME_AUTHORITY`. По умолчанию используется `https://localhost:7163/`, как в профиле запуска Identity. Для локальной проверки без HTTPS-сертификата используйте `http://127.0.0.1:5201/` и запускайте Identity так:

```powershell
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5201'
$env:OpenIddict__Issuer = 'http://127.0.0.1:5201/'
dotnet run --project DevHome.Identity/DevHome.Identity --no-launch-profile
```

## Проверка вручную

1. Запустите Identity и DevHome.
2. Откройте DevHome и нажмите «Войти» — откроется страница Identity.
3. Войдите демо-учётной записью; DevHome покажет имя, email и роли.
4. Для CLI запустите `DevHome.Cli`, откройте выданную ссылку, введите код и подтвердите вход.

## Безопасность

- Публичные клиенты обязаны использовать PKCE; сервер требует `code_challenge`.
- В access- и identity-токены попадает `sid` — идентификатор сеанса Identity. По нему
  можно связать токен с записью `UserSession` и отозвать конкретный вход.
- Отзыв сеанса в Identity отзывает токены приложений. Клиентское приложение узнает
  об этом при следующем обращении к API; принудительный back-channel logout не реализован.
- Consent для демонстрационных клиентов установлен в `implicit` (без экрана согласия). Для реальных приложений включите явное согласие.
- В Development разрешён HTTP. В промышленной среде оставьте только HTTPS и постоянные ключи.
- Секреты веб- и сервисного клиентов не храните в репозитории: используйте user-secrets/переменные окружения.
