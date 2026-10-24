# Подключение и работа: сервер и Blazor WebAssembly

Библиотека одна и та же, но окружения различаются:

| | Сервер (ASP.NET Core) | Blazor WebAssembly |
|---|---|---|
| Конфигурация | `appsettings.json` + `IConfiguration` | `wwwroot/appsettings.json`, загружается один раз |
| Горячая перезагрузка | автоматически (`reloadOnChange: true`) | только программно: обновить конфигурацию и вызвать `Reload()` |
| HTTP-транспорт | `SocketsHttpHandler` | `fetch` (`BrowserHttpMessageHandler`) |
| Потоки | нативная многопоточность | один поток, всё асинхронное |
| Circuit breaker | общий для процесса | состояние на вкладку, сбрасывается при перезагрузке страницы |
| Тримминг/AOT | обычно выключены | Release-публикация триммится, AOT — опция |
| JSON | reflection или source-gen | для AOT — только source-gen |
| Секреты в конфигурации | допустимы (файл на сервере) | **недопустимы**: файл публичен |

---

# Часть 1. Сервер (ASP.NET Core)

## 1.1 Регистрация

```csharp
using Common.Helper.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResilientHttpClient<IGitHubApi, GitHubApi>(
    builder.Configuration,
    "HttpClients:GitHub");

// или именованный клиент
builder.Services.AddResilientHttpClient(
    "GitHub",
    builder.Configuration,
    "HttpClients:GitHub");

var app = builder.Build();
```

`WebApplicationBuilder` подключает `appsettings.json` и `appsettings.{Environment}.json`
с `reloadOnChange: true`, поэтому параметры перезагружаются автоматически.

## 1.2 Конфигурация

`appsettings.json`:

```json
{
  "HttpClients": {
    "GitHub": {
      "BaseAddress": "https://api.github.com/",
      "Timeout": "00:00:30",
      "DefaultHeaders": {
        "User-Agent": "PersonalKit.Server",
        "Accept": "application/vnd.github+json"
      },
      "Resilience": {
        "Enabled": true,
        "Retry": {
          "Enabled": true,
          "MaxRetryAttempts": 3,
          "Delay": "00:00:01",
          "MaxDelay": "00:00:10",
          "BackoffType": "Exponential",
          "UseJitter": true,
          "RetryStatusCodes": [408, 429, 500, 502, 503, 504],
          "RetryNonIdempotentMethods": true
        },
        "CircuitBreaker": {
          "Enabled": true,
          "FailureRatio": 0.5,
          "MinimumThroughput": 100,
          "SamplingDuration": "00:00:30",
          "BreakDuration": "00:00:05"
        },
        "AttemptTimeout": {
          "Enabled": true,
          "Timeout": "00:00:10"
        }
      }
    }
  }
}
```

Для сервера можно включить `RetryNonIdempotentMethods: true`, если операции POST/PATCH
идемпотентны (например, поддерживают ключ идемпотентности). По умолчанию повторы
выполняются только для GET/HEAD/OPTIONS/TRACE/PUT/DELETE.

## 1.3 Типизированный клиент

```csharp
public interface IGitHubApi
{
    Task<ApiResponse<RepositoryDto>> GetRepositoryAsync(string owner, string name, CancellationToken ct);
    Task<ApiResponse<IssueDto>> CreateIssueAsync(string owner, string name, CreateIssueDto issue, CancellationToken ct);
}

public sealed class GitHubApi(HttpClient httpClient) : IGitHubApi
{
    public Task<ApiResponse<RepositoryDto>> GetRepositoryAsync(string owner, string name, CancellationToken ct) =>
        httpClient.NewRequest($"repos/{owner}/{name}").SendJsonAsync<RepositoryDto>(ct);

    public Task<ApiResponse<IssueDto>> CreateIssueAsync(string owner, string name, CreateIssueDto issue, CancellationToken ct) =>
        httpClient.NewRequest($"repos/{owner}/{name}/issues", HttpMethod.Post)
            .WithJsonContent(issue)
            .SendJsonAsync<IssueDto>(ct);
}
```

Использование:

```csharp
public sealed class RepositoryService(IGitHubApi github)
{
    public async Task<string> GetAsync(CancellationToken ct)
    {
        var response = await github.GetRepositoryAsync("dotnet", "runtime", ct);
        return response.EnsureSuccess().Content!.FullName;
    }
}
```

## 1.4 AOT-friendly JSON (source generation)

Для trimming/AOT (например, если сервер публикуется как NativeAOT):

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RepositoryDto))]
[JsonSerializable(typeof(IssueDto))]
[JsonSerializable(typeof(CreateIssueDto))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
```

```csharp
httpClient.NewRequest("api/items", HttpMethod.Post)
    .WithJsonContent(dto, ApiJsonContext.Default.CreateIssueDto)
    .SendJsonAsync(ApiJsonContext.Default.IssueDto, ct);
```

Перегрузки без `JsonTypeInfo` помечены `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`:
на сервере с reflection они работают как раньше, при включённом анализе тримминга компилятор
подскажет заменить их на source-gen.

## 1.5 Ошибки и повторы

```csharp
try
{
    var result = await client.NewRequest("api/items/42").SendJsonAsync<ItemDto>(ct);
    var item = result.EnsureSuccess().Content; // HttpRequestException для не-2xx
}
catch (HttpRequestException ex)
{
    // сеть недоступна или повторы исчерпаны
}
catch (BrokenCircuitException)
{
    // предохранитель разомкнут — запрос даже не отправлялся
}
catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
{
    // истёк общий Timeout
}
```

Повторы не скрывают код ошибки: если все попытки вернули, например, 503, клиент вернёт
последний `HttpResponseMessage`/`ApiResponse<T>` с этим кодом.

## 1.6 Тесты сервиса

```csharp
var services = new ServiceCollection();
services.AddLogging();
services
    .AddResilientHttpClient<IGitHubApi, GitHubApi>(configuration, "HttpClients:GitHub")
    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler());
```

## 1.7 Что учесть на сервере

- предохранитель общий для процесса — он реально защищает зависимость от каскадных сбоев;
- `RetryNonIdempotentMethods` включайте осознанно;
- HTTP-клиенты должны быть типизированными/именованными — `HttpClient` не создаётся вручную;
- секреты (токены, ключи API) можно передавать через конфигурацию/секреты, но не логировать их.

---

# Часть 2. Blazor WebAssembly

## 2.1 Регистрация

```csharp
using Common.Helper.Http;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddResilientHttpClient<ICatalogApi, CatalogApi>(
    builder.Configuration,
    "HttpClients:Catalog");

await builder.Build().RunAsync();
```

`WebAssemblyHostBuilder.CreateDefault` сам читает `wwwroot/appsettings.json`
и `wwwroot/appsettings.{Environment}.json`. Эти файлы **скачиваются в браузер** — никаких секретов.

## 2.2 Конфигурация для браузера

`wwwroot/appsettings.json`:

```json
{
  "HttpClients": {
    "Catalog": {
      "BaseAddress": "https://api.example.com/",
      "Timeout": "00:00:15",
      "DefaultHeaders": {
        "Accept": "application/json"
      },
      "Resilience": {
        "Enabled": true,
        "Retry": {
          "Enabled": true,
          "MaxRetryAttempts": 2,
          "Delay": "00:00:00.300",
          "MaxDelay": "00:00:02",
          "BackoffType": "Exponential",
          "UseJitter": true
        },
        "CircuitBreaker": {
          "Enabled": false
        },
        "AttemptTimeout": {
          "Enabled": true,
          "Timeout": "00:00:10"
        }
      }
    }
  }
}
```

Почему так:

- 2 повтора вместо 3 — пользователь не должен ждать долго;
- `RetryNonIdempotentMethods` по умолчанию `false`: POST/PATCH из формы не будут отправлены дважды;
- `CircuitBreaker: false` — состояние живёт на вкладку и не защищает сервер; проще отключить;
- меньший `Timeout` соответствует ожиданиям интерактивного интерфейса.

## 2.3 Вызовы из компонентов

```csharp
@inject ICatalogApi Catalog

@code {
    private IReadOnlyList<ItemDto>? _items;

    protected override async Task OnInitializedAsync()
    {
        var response = await Catalog.GetItemsAsync(CancellationToken.None);
        _items = response.EnsureSuccess().Content;
    }
}
```

Клиент:

```csharp
public sealed class CatalogApi(HttpClient httpClient) : ICatalogApi
{
    public Task<ApiResponse<List<ItemDto>>> GetItemsAsync(CancellationToken ct) =>
        httpClient.NewRequest("api/items")
            .WithQuery("page", 1)
            .SendJsonAsync(CatalogJsonContext.Default.ListItemDto, ct);
}
```

Абсолютные адреса и относительные (`api/items`) работают одинаково: базовый адрес
подставляет обработчик, поэтому его можно менять без пересоздания клиента.

## 2.4 AOT и тримминг

Blazor Release-публикация включает IL-триммер; AOT — опция
(`<RunAOTCompilation>true</RunAOTCompilation>`). Для AOT используйте только source-gen JSON:

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ItemDto))]
[JsonSerializable(typeof(List<ItemDto>))]
internal sealed partial class CatalogJsonContext : JsonSerializerContext;
```

```csharp
httpClient.NewRequest("api/items", HttpMethod.Post)
    .WithJsonContent(dto, CatalogJsonContext.Default.ItemDto);

var response = await httpClient
    .NewRequest("api/items")
    .SendJsonAsync(CatalogJsonContext.Default.ListItemDto, ct);
```

Reflection-перегрузки (`WithJsonContent(value)`, `SendJsonAsync<T>()`, `AddJson(name, value)`)
в AOT-сборке дадут предупреждения IL2026/IL3050 — используйте варианты с `JsonTypeInfo<T>`.

Проверка перед выкладкой:

```powershell
dotnet publish Client -c Release -p:RunAOTCompilation=true
```

и прогон ключевых сценариев в браузере: вход, список, форма, загрузка файла.

## 2.5 Горячая перезагрузка в браузере

Автоматической перезагрузки нет: в браузере нет файловой системы и `reloadOnChange` не работает.
Параметры фиксируются на время жизни вкладки. Если они должны обновляться, обновляйте
конфигурацию программно:

```csharp
// 1. Провайдер, который умеет заменять данные и вызывать reload.
public sealed class RemoteSettingsProvider : ConfigurationProvider
{
    public void Update(IDictionary<string, string?> data)
    {
        Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}

public sealed class RemoteSettingsSource(RemoteSettingsProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}
```

```csharp
// 2. Регистрация перед AddResilientHttpClient.
var remoteSettings = new RemoteSettingsProvider();
builder.Configuration.Add(new RemoteSettingsSource(remoteSettings));
```

```csharp
// 3. Периодическое обновление: сервер отдаёт плоский словарь ключей конфигурации.
public sealed class SettingsRefresher(HttpClient http, RemoteSettingsProvider settings) : IDisposable
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(5));

    public async Task RunAsync(CancellationToken ct)
    {
        while (await _timer.WaitForNextTickAsync(ct))
        {
            var data = await http.GetFromJsonAsync<Dictionary<string, string?>>("api/http-settings", ct);
            if (data is not null)
            {
                settings.Update(data); // IOptionsMonitor увидит изменения, конвейер пересоберётся
            }
        }
    }

    public void Dispose() => _timer.Dispose();
}
```

Серверный эндпоинт `GET /api/http-settings` возвращает, например:

```json
{
  "HttpClients:Catalog:BaseAddress": "https://api.example.com/",
  "HttpClients:Catalog:Retry:MaxRetryAttempts": "2",
  "HttpClients:Catalog:Timeout": "00:00:15"
}
```

Важно: после `Update` новые параметры применяются к **следующим** запросам во всех
экземплярах клиента на вкладке; конвейер устойчивости пересобирается. Секреты через такой
эндпоинт передавать нельзя.

Если настройки меняются только при релизе — проще оставить их в `wwwroot/appsettings.json`
и обновлять страницу после деплоя.

## 2.6 Загрузка и выгрузка файлов

Браузер буферизует тело запроса целиком, а обработчик библиотеки ещё и один раз читает
его в `byte[]` для повторов. Для больших файлов:

```csharp
// в конфигурации клиента для uploads
"Retry": { "MaxRetryAttempts": 0 }
```

```csharp
httpClient.NewRequest("api/files", HttpMethod.Post)
    .WithMultipartFormData(form => form
        .AddField("description", "report")
        .AddFile("file", fileStream, "report.pdf", "application/pdf"))
    .SendAsync(ct);

// большие ответы — без буферизации целиком
using var response = await httpClient
    .NewRequest($"api/files/{id}")
    .WithCompletionOption(HttpCompletionOption.ResponseHeadersRead)
    .SendAsync(ct);
```

## 2.7 CORS, заголовки и авторизация

- `Authorization` и кастомные заголовки вызывают preflight — сервер должен разрешить их в CORS;
- браузер отдаёт только заголовки из `Access-Control-Expose-Headers`, поэтому
  `ApiResponse.Headers` покажет не всё;
- токены получайте в браузере (OIDC/MSAL) и передавайте через `WithBearerToken(token)`;
  в `appsettings.json` их хранить нельзя.

## 2.8 Размер бандла

Замер по debug-графу библиотеки: около 1 МБ IL, ~467 КБ в gzip (Polly, Microsoft resilience,
телеметрия, опции). В Release-публикации Blazor работает триммер, а сервер отдаёт brotli,
поэтому реальная добавка меньше. Проверяйте `dotnet publish Client -c Release` и сравнивайте
`wwwroot/_framework`.

Если размер критичен:

- оставьте `CircuitBreaker: false` (код останется в сборке, но поведение проще);
- при отказе от устойчивости используйте обычный `AddHttpClient` + fluent-обёртку;
- при необходимости разделите библиотеку на `Common.Helper.Http` (fluent + опции)
  и `Common.Helper.Http.Resilience` (хендлер + Polly) — тогда браузерный клиент
  не тянет resilience-зависимости.

## 2.9 Тесты клиента

Тесты выполняются на сервере (xUnit/NUnit) с подменой первичного обработчика — тот же приём,
что и для серверных сервисов:

```csharp
services
    .AddResilientHttpClient<ICatalogApi, CatalogApi>(configuration, "HttpClients:Catalog")
    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler());
```

Компоненты Blazor тестируются отдельно (bUnit), сеть в них не нужна.

---

## Краткий чек-лист

| Вопрос | Сервер | WASM |
|---|---|---|
| Где настройки | `appsettings*.json` | `wwwroot/appsettings.json` |
| Секреты | можно | нельзя |
| Hot reload | автоматически | вручную (периодический fetch + `Reload`) |
| Retry POST/PATCH | по необходимости (`RetryNonIdempotentMethods`) | оставить `false` |
| Circuit breaker | включён | выключен |
| JSON для AOT | source-gen при NativeAOT | source-gen обязательно для AOT |
| Загрузка файлов | без ограничений | отключить повторы, следить за памятью |
| Тесты | подмена первичного обработчика | то же, тесты вне браузера |
