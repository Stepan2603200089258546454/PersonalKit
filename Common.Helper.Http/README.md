# Common.Helper.Http

Библиотека-шаблон для подключения типизированного `HttpClient` в DI с устойчивостью
(`Microsoft.Extensions.Http.Resilience`, Polly v8), **полной горячей перезагрузкой параметров**
из `appsettings.json` и fluent-обёрткой для составления и отправки запросов.

## Возможности

- регистрация типизированного или именованного клиента в DI одной строкой;
- параметры из конфигурации: базовый адрес, заголовки, таймауты, повторы, предохранитель;
- горячая перезагрузка всех параметров без перезапуска приложения;
- fluent-запросы: JSON, `application/x-www-form-urlencoded`, `multipart/form-data`
  (поля, файлы, потоки, байты, JSON-части), текст, байты, поток, готовое `HttpContent`;
- параметры строки запроса (включая коллекции и флаги), заголовки, Bearer/Basic-аутентификация;
- чтение JSON-ответа в модель с кодом ответа, заголовками и исходным телом;
- совместимость с trimming и AOT: перегрузки с `JsonTypeInfo<T>` для source-generated JSON,
  сборка помечена `IsTrimmable`/`IsAotCompatible`;
- безопасные для браузера повторы: POST/PATCH по умолчанию не повторяются;
- тело запроса не копируется, если запрос не может быть повторён: потоковая выгрузка
  в браузере не требует лишней копии в памяти;
- ошибка с телом не в формате модели (HTML-страница 500) не превращается в `JsonException`:
  тело остаётся в `RawContent` для `EnsureSuccess`;
- параметры проверяются по границам Polly заранее — вместо исключения при первом запросе
  приходит понятное сообщение `OptionsValidationException`.

## Структура

Один класс — один файл; классы сгруппированы по папкам:

```
Common.Helper.Http/
├── Configurations/        модели настроек, валидатор и границы значений
│   ├── ResilientHttpClientOptions.cs
│   ├── ResilienceOptions.cs
│   ├── RetryOptions.cs
│   ├── CircuitBreakerOptions.cs
│   ├── TimeoutOptions.cs
│   ├── ResilienceLimits.cs
│   └── ResilientHttpClientOptionsValidator.cs
├── DependencyInjection/   регистрация в DI
│   └── ServiceCollectionExtensions.cs
├── Http/                  обработчик, fluent-построитель, расширения
│   ├── ResilientHttpMessageHandler.cs
│   ├── HttpRequestBuilder.cs
│   ├── HttpClientExtensions.cs
│   ├── QueryStringBuilder.cs
│   ├── QueryParameter.cs
│   ├── QueryValueFormatter.cs
│   └── Multipart/
│       ├── MultipartFormDataBuilder.cs
│       └── MultipartPart.cs
├── Models/                модели ответа
│   ├── ApiResponse.cs
│   └── HttpStatus.cs
├── Resilience/            конвейер устойчивости и его кэш
│   ├── ResiliencePipelineFactory.cs
│   ├── ResiliencePipelineCache.cs
│   └── ResiliencePipelineCacheEntry.cs
├── README.md
├── usage-server-and-wasm.md   примеры для сервера и Blazor WASM
├── usage-cases.md             пары «клиент ↔ сервер» для типовых сценариев
└── appsettings.example.json
```

Пространство имён у всех типов одно — `Common.Helper.Http`; папки задают только физическую структуру.

## Требования

- .NET 10 (`net10.0`);
- пакеты: `Microsoft.Extensions.Http.Resilience` (транзитивно тянет `Microsoft.Extensions.Http`
  и `Polly.Core`) и `Microsoft.Extensions.Options.ConfigurationExtensions`;
- версии пакетов задаются централизованно в `Directory.Packages.props` репозитория.

## Подключение

Внутри этого решения — ссылкой на проект:

```xml
<ItemGroup>
  <ProjectReference Include="..\Common.Helper.Http\Common.Helper.Http.csproj" />
</ItemGroup>
```

При выносе библиотеки в отдельный пакет достаточно ссылки на `Common.Helper.Http`.

## Регистрация в DI

Типизированный клиент (рекомендуемый способ):

```csharp
using Common.Helper.Http;

builder.Services.AddResilientHttpClient<IGitHubApi, GitHubApi>(
    builder.Configuration,
    "HttpClients:GitHub");
```

Именованный клиент:

```csharp
builder.Services.AddResilientHttpClient(
    "GitHub",
    builder.Configuration,
    "HttpClients:GitHub");
```

Необязательные значения по умолчанию (конфигурация имеет приоритет):

```csharp
builder.Services.AddResilientHttpClient<IGitHubApi, GitHubApi>(
    builder.Configuration,
    "HttpClients:GitHub",
    options =>
    {
        options.Timeout = TimeSpan.FromSeconds(15);
        options.DefaultHeaders["User-Agent"] = "PersonalKit";
    });
```

Метод возвращает `IHttpClientBuilder`, поэтому можно добавить свои обработчики или
подменить первичный транспорт (см. «Тестирование приложения»).

## Конфигурация

Готовый пример со всеми параметрами и комментариями: [`appsettings.example.json`](appsettings.example.json).
Ниже тот же формат без комментариев — секция `appsettings.json` (все поля необязательны,
приведены значения по умолчанию):

```json
{
  "HttpClients": {
    "GitHub": {
      "BaseAddress": "https://api.github.com/",
      "Timeout": "00:00:30",
      "DefaultHeaders": {
        "Accept": "application/vnd.github+json",
        "User-Agent": "PersonalKit"
      },
      "Resilience": {
        "Enabled": true,
        "Retry": {
          "Enabled": true,
          "MaxRetryAttempts": 3,
          "Delay": "00:00:01",
          "MaxDelay": "00:00:30",
          "BackoffType": "Exponential",
          "UseJitter": true,
          "RetryStatusCodes": [408, 429, 500, 502, 503, 504],
          "RetryNonIdempotentMethods": false
        },
        "CircuitBreaker": {
          "Enabled": true,
          "FailureRatio": 0.5,
          "MinimumThroughput": 100,
          "SamplingDuration": "00:00:30",
          "BreakDuration": "00:00:05",
          "BreakStatusCodes": [408, 429, 500, 502, 503, 504]
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

| Параметр | Значение |
|---|---|
| `BaseAddress` | абсолютный адрес http/https. Без завершающего слэша трактуется как каталог: `https://host/v1` и `https://host/v1/` дают одинаковый результат |
| `Timeout` | общий таймаут запроса вместе со всеми повторами. `00:00:00` отключает ограничение; максимум — около 49 суток (ограничение `CancellationTokenSource`) |
| `DefaultHeaders` | добавляются к каждому запросу, если такого заголовка нет в самом запросе. Заголовки содержимого (`Content-Type` и подобные) применяются к телу, если оно есть |
| `Retry:MaxRetryAttempts` | число повторов после первой попытки (`3` — до 4 попыток), `0` отключает повторы |
| `Retry:Delay`, `MaxDelay` | базовая и максимальная задержка перед повтором. `MaxDelay: 0` — без верхней границы; обе не больше 1 суток |
| `Retry:BackoffType` | `Constant`, `Linear`, `Exponential` |
| `Retry:RetryStatusCodes` | коды ответа, при которых выполняется повтор; пустой массив — значения по умолчанию |
| `Retry:RetryNonIdempotentMethods` | повторять ли POST/PATCH. По умолчанию `false`: повтор может создать дубликат |
| `CircuitBreaker:FailureRatio` | доля ошибок в окне выборки, размыкающая предохранитель: больше 0 и не больше 1 |
| `CircuitBreaker:MinimumThroughput` | минимум вызовов в окне (не меньше 2), после которого предохранитель может разомкнуться |
| `CircuitBreaker:SamplingDuration`, `BreakDuration` | окно выборки и пауза разомкнутого состояния: от 500 мс до 1 суток |
| `AttemptTimeout` | таймаут одной попытки (от 10 мс до 1 суток); при истечении попытка считается неуспешной и может быть повторена |

Параметры проверяются при первом обращении к клиенту (и при каждой перезагрузке конфигурации).
Границы соответствуют требованиям Polly, поэтому невалидное значение не доходит до конвейера.
При ошибке бросается `OptionsValidationException` с описанием всех найденных проблем.

## Fluent-запросы

Точка входа — расширение `NewRequest` у `HttpClient` (в том числе у типизированного клиента):

```csharp
using var response = await httpClient
    .NewRequest("repos/dotnet/runtime")
    .WithQuery("page", 2)
    .WithHeader("X-Trace", "42")
    .WithAccept("application/vnd.github+json")
    .SendAsync(cancellationToken);
```

Метод можно задать вторым аргументом или через `WithMethod`:

```csharp
await httpClient.NewRequest("api/items", HttpMethod.Post).SendAsync(ct);
await httpClient.NewRequest("api/items").WithMethod(HttpMethod.Patch).SendAsync(ct);
```

### Параметры строки запроса

```csharp
httpClient.NewRequest("api/items")
    .WithQuery("page", 2)
    .WithQuery("search", "привет мир")
    .WithQuery("id", new[] { 1, 2, 3 })          // ?id=1&id=2&id=3
    .WithQuery("debug")                          // ?debug (без значения)
    .WithQuery(new Dictionary<string, string?>
    {
        ["sort"] = "name",
        ["filter"] = null, // пустое значение: filter=
    });
```

Правила:

- имена и значения кодируются по RFC 3986 (`Uri.EscapeDataString`), поэтому `&`, `=`, `+`, `#`,
  пробел и кириллица безопасны; параметры разделяются `&`, порядок и повторяющиеся имена сохраняются;
- существующая строка запроса дополняется, а фрагмент адреса остаётся в конце:
  `api/items#section` + `page=2` → `api/items?page=2#section`; завершающие `?` и `&` не дублируются;
- коллекция (массив, `List<T>`, любая `IEnumerable`, кроме `string`) раскрывается в несколько
  параметров с тем же именем; пустая коллекция не добавляет ничего, `null` внутри коллекции даёт
  пустое значение;
- значения форматируются в инвариантной культуре: `bool` → `true`/`false`, `DateTime`
  и `DateTimeOffset` → ISO 8601 с часовым поясом, `DateOnly` → `yyyy-MM-dd`, `TimeOnly` →
  `HH:mm:ss` с дробной частью при наличии, перечисления → имя, остальные типы — через
  `IFormattable`. Для нестандартного формата передайте уже готовую строку;
- `WithQuery(name)` добавляет флаг без `=`, `WithQuery(name, null)` — пустое значение `name=`.

### Заголовки и аутентификация

```csharp
httpClient.NewRequest("api/items")
    .WithHeader("X-Trace", "42")
    .WithHeaders(new Dictionary<string, string> { ["X-Source"] = "agent" })
    .WithAccept("application/json")
    .WithBearerToken(accessToken);
// или
httpClient.NewRequest("api/items").WithBasicAuthentication("user", "password");
```

`WithHeader(name, null)` удаляет ранее добавленный заголовок. Заголовки из
`DefaultHeaders` конфигурации применяются обработчиком и имеют меньший приоритет, чем заголовки запроса.
Заголовок содержимого (`Content-Type` и подобные) применяется к телу запроса и заменяет
значение, заданное содержимым: удобно, когда тип известен только коду вызова.

### JSON

```csharp
var result = await httpClient
    .NewRequest("api/items", HttpMethod.Post)
    .WithJsonContent(new { name = "item", price = 10 })
    .SendJsonAsync<ItemDto>(ct);

var item = result.EnsureSuccess().Content;
```

- по умолчанию используется `JsonSerializerDefaults.Web` (camelCase, регистронезависимое чтение);
- свои настройки: `.WithJsonSerializerOptions(options)` или второй аргумент `WithJsonContent(value, options)`;
- готовая строка JSON: `.WithJsonContent("{\"name\":\"raw\"}")` — без повторной сериализации;
  для другого медиатипа есть второй аргумент: `.WithJsonContent(json, "application/problem+json")`.

Для trimming/AOT используйте перегрузки с метаданными source-generated контекста:

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ItemDto))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

httpClient.NewRequest("api/items", HttpMethod.Post)
    .WithJsonContent(dto, ApiJsonContext.Default.ItemDto);

var response = await httpClient
    .NewRequest("api/items/42")
    .SendJsonAsync(ApiJsonContext.Default.ItemDto, ct);
```

Reflection-перегрузки (`WithJsonContent(value)`, `SendJsonAsync<T>()`, `AddJson(name, value)`)
помечены `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` и не подходят для полного
тримминга/AOT.

### Формы (`application/x-www-form-urlencoded`)

```csharp
await httpClient.NewRequest("login", HttpMethod.Post)
    .WithFormUrlEncodedContent(new Dictionary<string, string?>
    {
        ["user"] = "john",
        ["comment"] = "hello world",
        ["empty"] = null,
    })
    .SendAsync(ct);
```

### Multipart (`multipart/form-data`)

```csharp
await httpClient.NewRequest("upload", HttpMethod.Post)
    .WithMultipartFormData(form => form
        .AddField("title", "report")
        .AddJson("metadata", new { author = "agent" })
        .AddFile("document", fileStream, "report.pdf", "application/pdf")
        .AddBytes("preview", previewBytes, "preview.png", "image/png")
        .AddStream("payload", payloadStream, "application/octet-stream"))
    .SendAsync(ct);
```

### Текст, байты, поток, готовое содержимое

```csharp
httpClient.NewRequest("api/report", HttpMethod.Post)
    .WithStringContent("a;b", "text/csv");

httpClient.NewRequest("api/upload", HttpMethod.Post)
    .WithByteArrayContent(bytes, "application/octet-stream");

httpClient.NewRequest("api/upload", HttpMethod.Post)
    .WithStreamContent(stream, "application/octet-stream");

httpClient.NewRequest("api/items", HttpMethod.Post)
    .WithContent(new StreamContent(stream));
```

`WithoutContent()` убирает содержимое, если оно было задано ранее; последний вызов метода
содержимого побеждает.

### Ответ и ошибки

```csharp
var result = await httpClient
    .NewRequest("api/items/42")
    .SendJsonAsync<ItemDto>(ct);

if (!result.IsSuccessStatusCode)
{
    // result.StatusCode, result.Headers, result.RawContent доступны и для ошибок
    logger.LogWarning("Ошибка {StatusCode}: {Body}", result.StatusCode, result.RawContent);
}

var item = result.EnsureSuccess().Content; // бросит HttpRequestException для не-2xx
```

`SendJsonAsync` не бросает исключение на HTTP-коды ошибок. Для пустого тела `Content` = `null`.
Если тело неуспешного ответа не является JSON нужной модели (типичный случай — HTML-страница
`500` от прокси), исключение не бросается: `Content` остаётся `null`, а тело доступно в
`RawContent` и попадает в сообщение `EnsureSuccess`. Для успешного ответа невалидный JSON
по-прежнему даёт `JsonException` — это признак ошибки в контракте API.
Для произвольного тела используйте `SendAsync` и работайте с `HttpResponseMessage`.

### Повторная отправка

Построитель можно отправить повторно: JSON, форма, текст и байты создаются заново.
Потоки и `WithContent` переиспользуются, поэтому после первого чтения повторная отправка
потребует перемотки потока.

## Горячая перезагрузка

Параметры читаются через `IOptionsMonitor<ResilientHttpClientOptions>` на каждый запрос,
а конвейер устойчивости пересобирается при изменении параметров.

Требования:

1. файл конфигурации подключён с `reloadOnChange: true` (по умолчанию в `WebApplicationBuilder`
   и в консольном хосте с `AddJsonFile`);
2. клиент зарегистрирован методом `AddResilientHttpClient`.

Перезагружаются: `BaseAddress`, `DefaultHeaders`, `Timeout`, все параметры `Resilience`.
Не перезагружаются: состав обработчиков клиента (первичный обработчик и дополнительные
`DelegatingHandler`), он фиксируется при первом обращении к клиенту.

## Сервер и Blazor WebAssembly

Примеры для обоих окружений — в [`usage-server-and-wasm.md`](usage-server-and-wasm.md).
Готовые пары «клиент → сервер» для JSON, query, form, multipart, потоков, ошибок и
идемпотентности — в [`usage-cases.md`](usage-cases.md).

| | Сервер (ASP.NET Core) | Blazor WebAssembly |
|---|---|---|
| Конфигурация | `appsettings*.json` | `wwwroot/appsettings.json` (публичен) |
| Горячая перезагрузка | автоматически | вручную: обновить конфигурацию и вызвать `Reload()` |
| `RetryNonIdempotentMethods` | по необходимости | оставить `false` |
| Circuit breaker | включён | обычно выключен (состояние на вкладку) |
| JSON для AOT | source-gen при NativeAOT | source-gen обязателен для AOT |
| Секреты в конфигурации | допустимы | недопустимы |

## Поведение и ограничения

- `HttpClient.Timeout` равен `Timeout.InfiniteTimeSpan`: таймауты применяет обработчик, поэтому
  они могут меняться на лету. При срабатывании общего таймаута бросается `TaskCanceledException`
  с внутренним `TimeoutException` (как у штатного `HttpClient`).
- Тело запроса копируется в память только когда запрос действительно может быть повторён
  (метод идемпотентный или включён `RetryNonIdempotentMethods`, повторы не отключены).
  Если повторов нет — содержимое уходит в сеть без копии: потоковые выгрузки не занимают
  память целиком. Для больших выгрузок с включёнными повторами тело всё равно буферизуется,
  поэтому там лучше `Retry:MaxRetryAttempts: 0`.
- Базовый адрес нормализуется один раз и кэшируется до изменения значения в конфигурации.
- Промежуточные ответы неуспешных попыток освобождаются автоматически.
- Повторы и предохранитель считают неуспешными: `HttpRequestException`, таймаут попытки
  и коды ответа из `RetryStatusCodes` / `BreakStatusCodes`.
- По умолчанию повторяются только идемпотентные методы (`GET`, `HEAD`, `OPTIONS`, `TRACE`,
  `PUT`, `DELETE`). Для `POST`/`PATCH` повторы включаются параметром
  `RetryNonIdempotentMethods: true` — делать это стоит только при поддержке идемпотентности
  на сервере.
- `HttpClient.BaseAddress` содержит технический адрес `https://dynamic-http-client.invalid/`:
  через него относительные адреса доходят до обработчика, который подставляет реальный адрес.
  Если переопределить `BaseAddress` вручную через `ConfigureHttpClient`, приоритет получит
  это значение, а `BaseAddress` из конфигурации перестанет применяться.

## Обработка ошибок

| Исключение | Когда возникает |
|---|---|
| `OptionsValidationException` | невалидные параметры клиента |
| `TaskCanceledException` (внутри `TimeoutException`) | истёк общий `Timeout` |
| `OperationCanceledException` | отменён токен вызывающего кода |
| `HttpRequestException` | транспортная ошибка после исчерпания повторов |
| `BrokenCircuitException` | предохранитель разомкнут, вызов отклонён без отправки |
| `JsonException` | тело успешного ответа в `SendJsonAsync` не является JSON нужной модели |

При исчерпании повторов по коду ответа исключение не бросается — возвращается последний
`HttpResponseMessage` (например, `500`).

## Тестирование приложения

В тестах подменяйте первичный обработчик — устойчивость, параметры и обработчик остаются настоящими:

```csharp
services
    .AddResilientHttpClient<IGitHubApi, GitHubApi>(configuration, "HttpClients:GitHub")
    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler());
```

Сами тесты библиотеки лежат в проекте `Testing/Common.Helper.Http.Tests`
(в обозревателе решений — папка `Common/Testing`):

```powershell
# в песочнице DSH (in-process runner)
dotnet run --project Testing

# в обычном окружении и в Visual Studio
dotnet test PersonalKit.slnx -m:1
```

Покрыты параметры и валидация (включая границы Polly), устойчивость, горячая перезагрузка,
JSON, формы, multipart, потоки, байты, заголовки, query (форматирование, коллекции, флаги,
кодирование), ответы и ошибки fluent-обёртки, буферизация тела, кэш конвейеров и журналирование.
Сейчас это 333 теста.

## Сборка решения

```powershell
dotnet build PersonalKit.slnx -m:1
```

Результаты сборки складываются в `artifacts/` (настроено в `Directory.Build.props`).
