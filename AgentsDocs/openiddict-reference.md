# OpenIddict в DevHome.Identity: что за что отвечает

Справочник по настройкам и функциям OpenIddict, подключённым в `DevHome.Identity`.
Код: `DevHome.Identity/OpenIddict/*`, `Controllers/AuthorizationController.cs`, `Controllers/UserInfoController.cs`.
Сценарии входа: [devhome-oauth.md](devhome-oauth.md).
Админ-панель и личный кабинет: [identity-administration.md](identity-administration.md).

## 1. Три части OpenIddict

| Часть | Где подключается | За что отвечает |
|---|---|---|
| `AddCore` | `OpenIddictConfiguration` | Хранилище клиентов, областей, разрешений и токенов в PostgreSQL (`DBContext`, `UseEntityFrameworkCore`). |
| `AddServer` | `OpenIddictConfiguration` | Выдача кодов и токенов, проверка клиентов, подпись и шифрование токенов. |
| `AddValidation` | `OpenIddictConfiguration` | Проверка access-токена на защищённых конечных точках самого Identity (`/connect/userinfo`). `UseLocalServer` — проверка по локальным ключам, без сетевого запроса к себе. |

## 2. Конечные точки

| Настройка | Адрес | Назначение | Кто обрабатывает |
|---|---|---|---|
| `SetAuthorizationEndpointUris` | `/connect/authorize` | Начало входа: проверка входа пользователя, выдача кода авторизации. | `AuthorizationController.Authorize` |
| `SetTokenEndpointUris` | `/connect/token` | Обмен кода, refresh-токена, кода устройства или данных сервиса на токены. | OpenIddict (+ свой обработчик client credentials) |
| `SetUserInfoEndpointUris` | `/connect/userinfo` | Профиль пользователя по access-токену. | `UserInfoController` |
| `SetEndSessionEndpointUris` | `/connect/logout` | Выход: закрытие сеансов Identity и клиента. | `AuthorizationController.Logout` |
| `SetDeviceAuthorizationEndpointUris` | `/connect/deviceauthorization` | Выдача кода устройства для CLI. | OpenIddict |
| `SetEndUserVerificationEndpointUris` | `/connect/verify` | Страница, где пользователь вводит и подтверждает код устройства. | `AuthorizationController.Verify` |

Адреса относительные: итоговый адрес = базовый адрес приложения + путь, например `https://localhost:7163/connect/token`.
Метаданные провайдера и ключи публикуются в `/.well-known/openid-configuration` и `/.well-known/jwks`.

## 3. Разрешённые потоки (grant types)

| Метод настройки | Что разрешает | Кто использует | Почему так |
|---|---|---|---|
| `AllowAuthorizationCodeFlow()` | Вход через браузер с обменом кода на токен. | DevHome, MAUI, WPF | Код авторизации не попадает в историю браузера в виде токена. |
| `RequireProofKeyForCodeExchange()` | Требует `code_challenge`/`code_verifier` (PKCE) для всех клиентов. | все клиенты авторизации | Перехваченный код без verifier бесполезен; обязательно для публичных клиентов. |
| `AllowRefreshTokenFlow()` | Продление сеанса без повторного входа. | DevHome, MAUI, WPF, CLI | Refresh-токен выдаётся только при области `offline_access`. |
| `AllowDeviceAuthorizationFlow()` | Вход на устройстве без браузера. | CLI | Пользователь вводит код на телефоне/ПК, устройство опрашивает токен-эндпоинт. |
| `AllowClientCredentialsFlow()` | Токен от имени сервиса без пользователя. | `devhome-service` | Межсервисные вызовы, где нет пользователя. |

`DisableAccessTokenEncryption()` — access-токен остаётся JWT в открытом виде с подписью. Клиенты и API проверяют его сами, без обращения к Identity на каждый запрос. Если убрать строку, токен станет зашифрованным и читаемым только сервером.

## 4. Время жизни токенов

| Метод | Значение | Смысл |
|---|---|---|
| `SetAccessTokenLifetime` | 1 час | Ограничивает окно компрометации access-токена. |
| `SetRefreshTokenLifetime` | 14 дней | После этого нужен повторный вход. |
| `SetDeviceCodeLifetime` | 10 минут | Забытый код устройства быстро становится недействительным. |

## 5. Ключи подписи и шифрования

| Настройка | Окружение | Что делает |
|---|---|---|
| `AddEphemeralEncryptionKey()` / `AddEphemeralSigningKey()` | Development | Ключи создаются в памяти: не нужен сертификат в хранилище Windows. Минус — после перезапуска старые токены недействительны. |
| `AddDevelopmentEncryptionCertificate()` / `AddDevelopmentSigningCertificate()` | не Development | Сертификаты разработчика. Для промышленной среды нужны постоянные сертификаты из хранилища. |

В рабочем окружении постоянные сертификаты обязательны: иначе токены перестанут проверяться после перезапуска и на нескольких экземплярах сервиса одновременно.

## 6. Интеграция с ASP.NET Core

| Метод | Что делает |
|---|---|
| `UseAspNetCore()` | Включает обработку запросов OpenIddict в конвейере ASP.NET Core. |
| `EnableAuthorizationEndpointPassthrough()` | Передаёт `/connect/authorize` контроллеру: вход пользователя делает приложение. |
| `EnableEndSessionEndpointPassthrough()` | Передаёт `/connect/logout` контроллеру. |
| `EnableUserInfoEndpointPassthrough()` | Передаёт `/connect/userinfo` контроллеру. |
| `EnableEndUserVerificationEndpointPassthrough()` | Передаёт `/connect/verify` контроллеру (форма кода устройства). |
| `DisableTransportSecurityRequirement()` | Только Development: разрешает HTTP для localhost. В рабочем окружении HTTPS обязателен. |

Без passthrough OpenIddict отвечал бы сам, и приложение не смогло бы показать свою страницу входа.

## 7. Издатель токенов (issuer)

`OpenIddict:Issuer` в настройках задаёт адрес, который попадает в claim `iss` и публикуется в метаданных. Если значение не задано, OpenIddict берёт адрес текущего запроса, поэтому `localhost` и имя хоста в рабочем окружении дают разные issuer, и клиент, запомнивший токен по одному адресу, не примет его по другому. В промышленной среде issuer задаётся явно.

## 8. Клиенты

| Понятие | Значение в проекте | Смысл |
|---|---|---|
| `ClientId` | `devhome-web`, `devhome-maui`, `devhome-wpf`, `devhome-cli`, `devhome-service` | Публичный «логин» приложения. |
| `ClientType = Public` | MAUI, WPF, CLI | Приложение не может хранить секрет; защита — PKCE и точный redirect URI. |
| `ClientType = Confidential` | DevHome (web), сервис | Есть `client_secret`, хранится только на сервере. |
| `ClientSecret` | `OpenIddict:Clients:Web:Secret`, `OpenIddict:Clients:Service:Secret` | Секрет конфиденциального клиента. В Development есть значения по умолчанию, в рабочем окружении обязательны свои. |
| `ConsentType = Implicit` | все клиенты | Экран согласия не показывается: приложения свои. Для сторонних приложений включается явное согласие. |
| `RedirectUris` / `PostLogoutRedirectUris` | `https://localhost:7270/signin-oidc`, `http://127.0.0.1:5180/callback`, `http://127.0.0.1:5181/callback` | Куда вернуть браузер с кодом и после выхода. Совпадение проверяется символ в символ. |
| `Requirements.Features.ProofKeyForCodeExchange` | MAUI, WPF | Обязательный PKCE на уровне клиента. |

### Разрешения клиента (`Permissions`)

| Разрешение | Что открывает |
|---|---|
| `Permissions.Endpoints.Authorization` | Доступ к `/connect/authorize`. |
| `Permissions.Endpoints.Token` | Доступ к `/connect/token`. |
| `Permissions.Endpoints.EndSession` | Доступ к `/connect/logout`. |
| `Permissions.Endpoints.DeviceAuthorization` | Доступ к `/connect/deviceauthorization`. |
| `Permissions.Endpoints.UserInfo` | В OpenIddict 7 отдельного разрешения нет: `/connect/userinfo` работает по access-токену. |
| `Permissions.GrantTypes.AuthorizationCode` | Сценарий кода авторизации. |
| `Permissions.GrantTypes.RefreshToken` | Продление сеанса. |
| `Permissions.GrantTypes.DeviceCode` | Сценарий кода устройства. |
| `Permissions.GrantTypes.ClientCredentials` | Сценарий «сервис-сервис». |
| `Permissions.ResponseTypes.Code` | Ответ в виде кода (не токена в URL). |
| `Permissions.Scopes.Profile`, `.Email` | Области профиля и почты. |
| `Permissions.Prefixes.Scope + DevHomeScopes.Api` | Область `devhome.api`. |

## 9. Области (scopes)

| Область | Что даёт | Кому нужна |
|---|---|---|
| `openid` | Идентификатор пользователя `sub`, включение OIDC. | всем клиентам с входом |
| `profile` | Имя и данные профиля. | DevHome, MAUI, WPF, CLI |
| `email` | Адрес почты и признак подтверждения. | DevHome, MAUI, WPF, CLI |
| `offline_access` | Право на refresh-токен. | клиентам с продлением сеанса |
| `devhome.api` | Право обращаться к API DevHome; кладёт ресурс в токен. | всем, кто вызывает API |

Области регистрируются через `RegisterScopes` и создаются в базе сидером (`OpenIddictSeeder`), поэтому клиент не может запросить область, которой нет в справочнике.

## 10. Свой обработчик client credentials

`ClientCredentialsHandler` — единственное место, где OpenIddict расширяется кодом.

| Элемент | Значение | Почему |
|---|---|---|
| `HandleTokenRequestContext` | контекст обработки `/connect/token` | Нужно вмешаться до выдачи токена. |
| `SetType(OpenIddictServerHandlerType.Custom)` | свой обработчик | Не заменяет стандартную логику, а дополняет её. |
| `SetOrder(int.MinValue + 100_000)` | раньше стандартных обработчиков | Встроенные проверки запроса уже выполнены, но токен ещё не выдан. |
| `context.Request.IsClientCredentialsGrantType()` | фильтр сценария | Остальные сценарии остаются стандартным обработчикам. |
| `identity.SetClaim(Claims.Subject, clientId)` | субъект = клиент | Пользователя в этом сценарии нет. |
| `context.SignIn(principal)` | завершение запроса | OpenIddict формирует ответ с токеном. |

## 11. Claim и их назначение

`AuthorizationController.CreatePrincipalAsync` собирает claim пользователя, `GetDestinations` решает, куда каждый попадёт.

| Claim | Значение | `AccessToken` | `IdentityToken` |
|---|---|---|---|
| `sub` | идентификатор пользователя | да | да |
| `name` | имя пользователя | да | да |
| `email` | адрес почты | да | да |
| `role` | роли Identity | да | да |
| прочие | — | да | нет |

`AccessToken` читает API, `IdentityToken` читает клиент. Пользовательские данные попадают в оба токена, чтобы клиенту не требовался лишний запрос в `/connect/userinfo`.

## 12. Настройки приложения

| Ключ | Назначение |
|---|---|
| `OpenIddict:Issuer` | Явный адрес издателя токенов. Пусто — адрес берётся из запроса. |
| `OpenIddict:Clients:Web:RedirectUri` | Адрес возврата веб-клиента (по умолчанию `https://localhost:7270/signin-oidc`). |
| `OpenIddict:Clients:Web:Secret` | Секрет веб-клиента. |
| `OpenIddict:Clients:Service:Secret` | Секрет сервисного клиента. |
| `OpenIddict:DemoUser:Email`, `OpenIddict:DemoUser:Password` | Демо-учётная запись (только Development). |

## 13. Что упрощено в Development и что менять для промышленной среды

| В Development | Для промышленной среды |
|---|---|
| Временные ключи в памяти | Постоянные сертификаты подписи и шифрования. |
| `DisableTransportSecurityRequirement()` — разрешён HTTP | Только HTTPS, строку убрать. |
| `UseMigrationsEndPoint()` | Убрать, миграции применять управляемо. |
| Демо-пользователь `demo@devhome.local` | Не создавать; сид только справочников. |
| Секреты по умолчанию для web и service | Секреты из user-secrets или переменных окружения. |
| Issuer вычисляется из запроса | Задать `OpenIddict:Issuer` явно. |
