# Шаблон API с защитой OpenIddict

`DevHome.Api` — минимальное приложение ASP.NET Core, которое показывает, как принимать
запросы двух видов:

- **публичные** — доступны без токена;
- **приватные** — требуют действующий access-токен, выданный `DevHome.Identity`.

API не читает базу Identity и не хранит ключи подписи. Он спрашивает Identity,
действителен ли токен, через endpoint introspection. Поэтому API можно запускать
отдельно от Identity и обновлять независимо.

## 1. Где находится

| Что | Путь |
|---|---|
| проект | `DevHome/DevHome.Api/DevHome.Api.csproj` |
| код | `DevHome/DevHome.Api/Program.cs` |
| настройки | `DevHome/DevHome.Api/appsettings.json` |
| Dockerfile | `DevHome/DevHome.Api/Dockerfile` |
| запуск в Docker | сервис `api` в `docker-compose.yml` |

В Docker API **не публикует порт на хост**. Он доступен только контейнерам сети
`devhome` и командам `docker compose exec`.

## 2. Эндпоинты

| Метод | Путь | Доступ | Назначение |
|---|---|---|---|
| `GET` | `/health` | без токена | проверка живости и версии |
| `GET` | `/` | без токена | краткая информация о сервисе |
| `GET` | `/api/public/ping` | без токена | простой публичный ответ |
| `GET` | `/api/public/info` | без токена | описание сервиса и список методов |
| `GET` | `/api/private/me` | токен | данные из токена: `sub`, `name`, `email`, `client_id`, роли, scopes |
| `GET` | `/api/private/data` | токен | пример защищённых данных |
| `GET` | `/api/private/admin` | токен + роль `Admin` | пример проверки роли |

Приватные методы возвращают:

- `401`, если токена нет, он истёк или introspection вернул `active: false`;
- `403`, если токен действителен, но не содержит роль `Admin` (для `/admin`).

## 3. Как проверяется токен

1. Клиент получает access-токен в `DevHome.Identity` (`/connect/token`).
2. Клиент отправляет запрос с заголовком:

   ```http
   Authorization: Bearer <access_token>
   ```

3. OpenIddict Validation в API отправляет токен в Identity:

   ```http
   POST /connect/introspect
   Authorization: Basic <devhome-api:secret>
   token=<access_token>
   ```

4. Если Identity отвечает `"active": true`, запрос считается аутентифицированным.
5. Роль `Admin` берётся из claim `role` access-токена.

Клиент `devhome-api` создаётся сидером Identity и имеет только разрешение
на introspection. В аудиторию токена добавлен `devhome-api`, иначе OpenIddict
запрещает клиенту проверять «чужой» токен.

## 4. Настройки

`DevHome/DevHome.Api/appsettings.json`:

```json
{
  "Http": {
    "UseHttpsRedirection": true
  },
  "OpenIddict": {
    "Issuer": "http://localhost:5201/",
    "Validation": {
      "ClientId": "devhome-api",
      "ClientSecret": "devhome-api-development-secret"
    }
  }
}
```

В Docker используется `DevHome/DevHome.Api/appsettings.Production.json`:
он отключает HTTPS-редирект и явно задаёт `ClientId` для introspection.
Этот файл примонтирован в контейнер и правится на хосте без пересборки.

Переменными окружения из `docker-compose.yml` остаются только
машинно-зависимые значения:

- `OpenIddict__Issuer` — публичный адрес Identity;
- `OpenIddict__Validation__ClientSecret` — `API_CLIENT_SECRET`.

`Issuer` в API должен совпадать с `OpenIddict:Issuer` в Identity, иначе introspection
отклонит токен как выданный другим сервером.

## 5. Запуск в Docker

API входит в общий compose:

```powershell
docker compose up -d --build
docker compose ps api
docker compose logs -f api
```

Публичный метод:

```powershell
docker compose exec -T api curl -fsS http://localhost:8080/api/public/ping
```

Ожидаемый ответ:

```json
{"status":"ok","message":"Публичный метод доступен без токена.","utc":"..."}
```

Получить сервисный токен и вызвать приватный метод:

```powershell
$json = docker compose exec -T api curl -fsS -X POST http://identity:8080/connect/token -d grant_type=client_credentials -d client_id=devhome-service -d client_secret=devhome-service-development-secret -d scope=devhome.api
$token = ($json | ConvertFrom-Json).access_token
docker compose exec -T api curl -fsS -H "Authorization: Bearer $token" http://localhost:8080/api/private/me
```

Проверка отказа:

```powershell
# без токена — 401
docker compose exec -T api curl -s -o NUL -w '%{http_code}' http://localhost:8080/api/private/me

# сервисный токен без роли Admin — 403
docker compose exec -T api curl -s -o NUL -w '%{http_code}' -H "Authorization: Bearer $token" http://localhost:8080/api/private/admin
```

## 6. Запуск без Docker (для отладки)

1. Запустите Identity на `http://localhost:5201`.
2. Убедитесь, что в базе есть клиент `devhome-api` (создаётся при старте Identity
   в Development или при `OpenIddict:Seed:RefreshClients=true`).
3. Запустите API:

   ```powershell
   dotnet run --project DevHome/DevHome.Api
   ```

4. API слушает `http://localhost:5301`, публичные методы открываются в браузере.

В Development API использует `appsettings.json`: issuer `http://localhost:5201/`,
секрет `devhome-api-development-secret`. Если Identity запущен в Docker, укажите
его адрес через переменные окружения.

## 7. Как добавить свой защищённый метод

```csharp
app.MapGet("/api/private/orders", (ClaimsPrincipal user) =>
{
    var subject = user.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
    return Results.Ok(new { subject, orders = Array.Empty<object>() });
})
.RequireAuthorization();
```

Для проверки роли:

```csharp
app.MapGet("/api/private/reports", () => Results.Ok(new { ok = true }))
   .RequireAuthorization("Admin");
```

Политика `Admin` уже зарегистрирована в `Program.cs` и проверяет claim `role`.
Чтобы роль попала в access-токен, пользователь должен входить через Identity
с областью `devhome.api`; `AuthorizationController` добавляет роли в токен.

## 8. Ограничения шаблона

- Нет базы данных и бизнес-логики: это каркас для копирования.
- Нет Swagger/OpenAPI: добавьте `Microsoft.AspNetCore.OpenApi`, если нужен интерфейс.
- Нет ограничения частоты запросов, кэширования и логирования аудита.
- API доверяет Identity, но не проверяет подпись токена локально: при недоступности
  Identity приватные методы будут возвращать `401`.
- Порт наружу не публикуется; если нужен доступ с хоста, добавьте `ports` в compose,
  но помните, что API защищён только токенами, а не HTTPS.
