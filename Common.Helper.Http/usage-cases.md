# Практические кейсы: клиент ↔ сервер

Готовые пары «код клиента `Common.Helper.Http` → код сервера ASP.NET Core» для типовых
сценариев: JSON, query, `application/x-www-form-urlencoded`, `multipart/form-data`, потоки,
скачивание файлов, сырой JSON/текст, ошибки, повтор POST.

Для каждого кейса даны оба варианта сервера: **Minimal API** и **контроллер MVC**.
Всё написано под .NET 10 и `System.Text.Json` с веб-настройками (camelCase) — это совпадает
с настройками библиотеки по умолчанию.

Оглавление:

1. [Контракты](#1-контракты)
2. [Клиент: регистрация и конфигурация](#2-клиент-регистрация-и-конфигурация)
3. [JSON: создать объект](#3-json-создать-объект)
4. [Query: список с фильтрами](#4-query-список-с-фильтрами)
5. [Form: вход по форме](#5-form-вход-по-форме)
6. [Multipart: поля + JSON + файл](#6-multipart-поля--json--файл)
7. [Поток: загрузка большого файла](#7-поток-загрузка-большого-файла)
8. [Поток: скачивание файла](#8-поток-скачивание-файла)
9. [Сырой JSON и произвольный текст](#9-сырой-json-и-произвольный-текст)
10. [Ошибки: ProblemDetails](#10-ошибки-problemdetails)
11. [Повтор POST и Idempotency-Key](#11-повтор-post-и-idempotency-key)
12. [Авторизация и отмена](#12-авторизация-и-отмена)
13. [Сводная таблица](#13-сводная-таблица)
14. [Чек-лист сервера](#14-чек-лист-сервера)
15. [Чек-лист клиента](#15-чек-лист-клиента)

---

## 1. Контракты

Модели можно держать в общем проекте `Contracts`, на который ссылаются и клиент, и сервер.

```csharp
namespace Cases.Contracts;

// JSON-тело запроса и ответа.
public sealed record CreateItemRequest(string Name, decimal Price, int Quantity, string? Comment);

public sealed record ItemDto(
    Guid Id,
    string Name,
    decimal Price,
    int Quantity,
    DateTimeOffset CreatedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// Ответ на ошибку: так выглядит ProblemDetails в ASP.NET Core.
public sealed record ProblemDto(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    Dictionary<string, string[]>? Errors,
    string? TraceId);

// Multipart: JSON-часть формы.
public sealed record UploadMetadata(string? Author, string[]? Tags);

public sealed record UploadResult(Guid Id, string FileName, long Size, UploadMetadata? Metadata);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);
```

Модели формы — **классы со свойствами** (так их одинаково хорошо привязывают и MVC, и Minimal API):

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Cases.Server;

public sealed class LoginForm
{
    [Required]
    public string User { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed class UploadForm
{
    public string? Description { get; set; }

    // JSON-часть формы приходит строкой.
    public string? Metadata { get; set; }

    // Имя свойства совпадает с именем части формы ("file").
    public IFormFile? File { get; set; }
}
```

---

## 2. Клиент: регистрация и конфигурация

`appsettings.json`:

```json
{
  "HttpClients": {
    "CasesApi": {
      "BaseAddress": "https://localhost:7150/",
      "Timeout": "00:02:00",
      "DefaultHeaders": {
        "Accept": "application/json"
      },
      "Resilience": {
        "Retry": {
          "MaxRetryAttempts": 2,
          "Delay": "00:00:00.300",
          "MaxDelay": "00:00:05",
          "RetryNonIdempotentMethods": false
        },
        "CircuitBreaker": {
          "Enabled": true,
          "MinimumThroughput": 20
        },
        "AttemptTimeout": {
          "Enabled": true,
          "Timeout": "00:00:30"
        }
      }
    }
  }
}
```

Регистрация типизированного клиента:

```csharp
using Common.Helper.Http;

builder.Services.AddResilientHttpClient<ICasesApi, CasesApi>(
    builder.Configuration,
    "HttpClients:CasesApi");
```

В коде клиента понадобятся также `using System.Net;` (коды ответа) и
`using System.Net.Http.Json;` (чтение JSON из `HttpContent`).

Минимальный типизированный клиент — остальные методы в кейсах показаны как вызовы `http`
(это тот же `HttpClient`, который внедрён в класс):

```csharp
using Common.Helper.Http;

public interface ICasesApi
{
    Task<ApiResponse<ItemDto>> CreateAsync(CreateItemRequest request, CancellationToken ct);
}

public sealed class CasesApi(HttpClient http) : ICasesApi
{
    public Task<ApiResponse<ItemDto>> CreateAsync(CreateItemRequest request, CancellationToken ct) =>
        http.NewRequest("api/cases/items", HttpMethod.Post)
            .WithJsonContent(request)
            .SendJsonAsync<ItemDto>(ct);
}
```

Сервер для всех кейсов (общая часть):

```csharp
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Cases.Server;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddAuthorization();

// Для Minimal API, который принимает большие файлы потоком.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 512L * 1024 * 1024);

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();
```

> В примерах ниже у обработчиков Minimal API указан явный тип возврата
> (`IResult` или `Task<IResult>`): так разные ветки (`Results.Ok`, `Results.BadRequest`,
> `Results.NotFound`) не конфликтуют при выводе типа лямбды.

---

## 3. JSON: создать объект

### Клиент

```csharp
var request = new CreateItemRequest("Квартальный отчёт", 199.90m, 2, "черновик");

ApiResponse<ItemDto> result = await http
    .NewRequest("api/cases/items", HttpMethod.Post)
    .WithHeader("X-Correlation-Id", Guid.NewGuid().ToString("N"))
    .WithJsonContent(request)
    .SendJsonAsync<ItemDto>(ct);

// 2xx — вернёт ItemDto; не-2xx — бросит HttpRequestException с кодом и телом.
ItemDto item = result.EnsureSuccess().Content!;
```

Для AOT/тримминга — source-gen перегрузка:

```csharp
http.NewRequest("api/cases/items", HttpMethod.Post)
    .WithJsonContent(request, CasesJsonContext.Default.CreateItemRequest)
    .SendJsonAsync(CasesJsonContext.Default.ItemDto, ct);
```

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateItemRequest))]
[JsonSerializable(typeof(ItemDto))]
public sealed partial class CasesJsonContext : JsonSerializerContext;
```

### Minimal API

```csharp
app.MapPost("/api/cases/items", IResult (CreateItemRequest request) =>
{
    var item = new ItemDto(
        Guid.NewGuid(),
        request.Name,
        request.Price,
        request.Quantity,
        DateTimeOffset.UtcNow);

    return Results.Created($"/api/cases/items/{item.Id}", item);
});
```

### Контроллер

```csharp
[ApiController]
[Route("api/cases")]
public sealed class CasesController : ControllerBase
{
    [HttpPost("items")]
    [ProducesResponseType<ItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<ItemDto> Create([FromBody] CreateItemRequest request)
    {
        var item = new ItemDto(
            Guid.NewGuid(),
            request.Name,
            request.Price,
            request.Quantity,
            DateTimeOffset.UtcNow);

        return Created($"/api/cases/items/{item.Id}", item);
    }
}
```

### Что важно

- Клиент сериализует в camelCase (`JsonSerializerDefaults.Web`), сервер читает так же — совпадает.
- `[ApiController]` сам возвращает `400` с `ValidationProblemDetails`, если модель не прошла
  `DataAnnotations`; Minimal API проверки не делает — возвращайте `Results.ValidationProblem`.
- `Content-Type: application/json; charset=utf-8` выставляется содержимым автоматически;
  сервер отвечает `415`, если тип не JSON.
- `X-Correlation-Id` из примера — обычный заголовок запроса; сервер может писать его в лог
  через `Request.Headers["X-Correlation-Id"]`.

---

## 4. Query: список с фильтрами

### Клиент

```csharp
var page = await http
    .NewRequest("api/cases/items")
    .WithQuery("page", 2)
    .WithQuery("pageSize", 20)
    .WithQuery("tags", new[] { "finance", "q3" })  // ?tags=finance&tags=q3
    .WithQuery("day", new DateOnly(2026, 10, 24))  // ?day=2026-10-24
    .WithQuery("minPrice", 100.5m)
    .WithQuery("sort", "name")
    .SendJsonAsync<PagedResult<ItemDto>>(ct);
```

Итоговый адрес:

```text
GET api/cases/items?page=2&pageSize=20&tags=finance&tags=q3&day=2026-10-24&minPrice=100.5&sort=name
```

### Minimal API

```csharp
app.MapGet("/api/cases/items", IResult (
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string[]? tags = null,
    [FromQuery] DateOnly? day = null,
    [FromQuery] decimal? minPrice = null,
    [FromQuery] string? sort = null) =>
{
    // tags — массив из повторяющихся параметров string[].
    var items = Array.Empty<ItemDto>();
    return Results.Ok(new PagedResult<ItemDto>(items, Total: 0, page, pageSize));
});
```

### Контроллер

Метод добавляется в `CasesController` из кейса 3:

```csharp
[HttpGet("items")]
[ProducesResponseType<PagedResult<ItemDto>>(StatusCodes.Status200OK)]
public ActionResult<PagedResult<ItemDto>> List(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string[]? tags = null,
    [FromQuery] DateOnly? day = null,
    [FromQuery] decimal? minPrice = null,
    [FromQuery] string? sort = null)
{
    var items = Array.Empty<ItemDto>();
    return Ok(new PagedResult<ItemDto>(items, Total: 0, page, pageSize));
}
```

### Что важно

- Массив на сервере собирается из **повторяющихся ключей** (`?tags=a&tags=b`), ровно так их
  отправляет `WithQuery("tags", new[] { ... })`. Запятые внутри значения не разделяются:
  `?tags=a,b` — это один элемент `"a,b"`.
- Отсутствующий параметр у `string[]?`/`int?` даёт `null`; у необязательного `string[]`
  (без `?`) — пустой массив.
- `DateOnly`, `TimeOnly`, `DateTimeOffset`, `Guid`, `decimal`, `bool` привязываются из
  ISO-формата, который формирует библиотека. Для `bool` клиент отправляет `true`/`false`.
- Переименование параметра: `[FromQuery(Name = "q")] string? search`.
- Если параметр обязателен (`int page` без значения по умолчанию) и его нет, Minimal API
  вернёт `400`, MVC — ошибку модели.

---

## 5. Form: вход по форме

### Клиент

```csharp
using var response = await http
    .NewRequest("api/cases/login", HttpMethod.Post)
    .WithFormUrlEncodedContent(new Dictionary<string, string?>
    {
        ["user"] = "demo",
        ["password"] = "secret",
        ["rememberMe"] = "true",
    })
    .SendAsync(ct);

response.EnsureSuccessStatusCode();
var login = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
```

### Minimal API

```csharp
app.MapPost("/api/cases/login", IResult ([FromForm] LoginForm form) =>
    {
        if (form.User == "demo" && form.Password == "secret")
        {
            return Results.Ok(new LoginResponse("token", DateTimeOffset.UtcNow.AddHours(1)));
        }

        return Results.Unauthorized();
    })
    .DisableAntiforgery();
```

### Контроллер

Метод добавляется в тот же `CasesController`:

```csharp
[HttpPost("login")]
[Consumes("application/x-www-form-urlencoded")]
[ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
public ActionResult<LoginResponse> Login([FromForm] LoginForm form)
{
    if (form.User != "demo" || form.Password != "secret")
    {
        return Unauthorized();
    }

    return Ok(new LoginResponse("token", DateTimeOffset.UtcNow.AddHours(1)));
}
```

### Что важно

- `WithFormUrlEncodedContent` кодирует пробел как `+`, значения — как в HTML-форме;
  сервер привязывает поля к свойствам `LoginForm` без учёта регистра (`user` → `User`).
- **Minimal API**: любая привязка формы по умолчанию требует antiforgery-токена. Для API
  с Bearer/API-key (без cookie) это лишнее — вызывайте `.DisableAntiforgery()`.
  Не отключайте её для браузерных форм с cookie-аутентификацией.
- **Контроллер**: `AddControllers()` не включает antiforgery-фильтры, поэтому токен не нужен.
  Если в приложении включён глобальный `AutoValidateAntiforgeryToken`, поставьте на действие
  `[IgnoreAntiforgeryToken]`.
- Ответ можно читать и через библиотеку: `SendJsonAsync<LoginResponse>` вместо `SendAsync`.

---

## 6. Multipart: поля + JSON + файл

### Клиент (сервер или консоль)

```csharp
await using var file = File.OpenRead("report.pdf");

using var response = await http
    .NewRequest("api/cases/uploads", HttpMethod.Post)
    .WithMultipartFormData(form => form
        .AddField("description", "Квартальный отчёт")
        .AddJson("metadata", new UploadMetadata("agent", ["q3", "finance"]))
        .AddFile("file", file, "report.pdf", "application/pdf"))
    .SendAsync(ct);

var upload = await response.Content.ReadFromJsonAsync<UploadResult>(ct);
```

### Клиент (Blazor WebAssembly)

```csharp
// IBrowserFile из <InputFile>; лимит задаётся при открытии потока.
await using var stream = browserFile.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);

using var response = await http
    .NewRequest("api/cases/uploads", HttpMethod.Post)
    .WithMultipartFormData(form => form
        .AddField("description", "Квартальный отчёт")
        .AddJson("metadata", new UploadMetadata("agent", ["q3", "finance"]))
        .AddFile("file", stream, browserFile.Name, browserFile.ContentType))
    .SendAsync(ct);
```

### Minimal API

```csharp
app.MapPost("/api/cases/uploads", async Task<IResult> (
    [FromForm] UploadForm form,
    IWebHostEnvironment environment,
    CancellationToken ct) =>
{
    if (form.File is not { Length: > 0 } file)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["Файл обязателен."],
        });
    }

    var metadata = string.IsNullOrWhiteSpace(form.Metadata)
        ? null
        : JsonSerializer.Deserialize<UploadMetadata>(form.Metadata, JsonSerializerOptions.Web);

    var id = Guid.NewGuid();
    var directory = Path.Combine(environment.ContentRootPath, "uploads");
    Directory.CreateDirectory(directory);

    var path = Path.Combine(directory, $"{id:N}{Path.GetExtension(file.FileName)}");
    await using (var target = File.Create(path))
    {
        await file.CopyToAsync(target, ct);
    }

    return Results.Ok(new UploadResult(id, file.FileName, file.Length, metadata));
})
.DisableAntiforgery();
```

### Контроллер

Отдельный контроллер для загрузок: адрес хранилища берётся из `IWebHostEnvironment`,
который внедряется в основной конструктор.

```csharp
[ApiController]
[Route("api/cases")]
public sealed class UploadsController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("uploads")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(200_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
    public async Task<ActionResult<UploadResult>> Upload(
        [FromForm] UploadForm form,
        CancellationToken ct)
    {
        if (form.File is not { Length: > 0 } file)
        {
            ModelState.AddModelError(nameof(form.File), "Файл обязателен.");
            return ValidationProblem(ModelState);
        }

        var metadata = string.IsNullOrWhiteSpace(form.Metadata)
            ? null
            : JsonSerializer.Deserialize<UploadMetadata>(form.Metadata, JsonSerializerOptions.Web);

        var id = Guid.NewGuid();
        var directory = Path.Combine(environment.ContentRootPath, "uploads");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{id:N}{Path.GetExtension(file.FileName)}");
        await using (var target = System.IO.File.Create(path))
        {
            await file.CopyToAsync(target, ct);
        }

        return Ok(new UploadResult(id, file.FileName, file.Length, metadata));
    }
}
```

### Что важно

- Имена частей совпадают со свойствами: `file` → `File`, `description` → `Description`,
  `metadata` → `Metadata` (без учёта регистра).
- JSON-часть (`AddJson`) приходит как обычное строковое поле с `Content-Type: application/json`;
  сервер разбирает его вручную. Если нужна автоматическая привязка, пишите кастомный
  `IModelBinder` или принимайте `string`.
- Несколько файлов: свойство `List<IFormFile> Files`, на клиенте — несколько
  `.AddFile("files", ...)` с одним именем.
- Лимиты: Kestrel по умолчанию 30 МБ, multipart — 128 МБ. Для больших файлов поднимайте оба
  (`MaxRequestBodySize`, `MultipartBodyLengthLimit`).
- Поток, переданный в `AddFile`, закрывается вместе с содержимым запроса после отправки —
  не используйте его после `SendAsync`.
- Повторы POST по умолчанию выключены, поэтому тело не копируется в память: файл уходит
  в сеть потоком.

---

## 7. Поток: загрузка большого файла

Если файл не нужно оборачивать в multipart (например, потоковая передача в хранилище),
отправляйте тело потоком.

### Клиент

Ниже — сервер/консоль. В браузере источником потока служит `IBrowserFile.OpenReadStream(...)`
(см. кейс 6), а не файловая система.

```csharp
await using var source = File.OpenRead("dump.bin");

using var response = await http
    .NewRequest("api/cases/streams", HttpMethod.Post)
    .WithStreamContent(source, "application/octet-stream")
    .WithHeader("X-File-Name", "dump.bin")
    .SendAsync(ct);

response.EnsureSuccessStatusCode();
```

### Minimal API

```csharp
app.MapPost("/api/cases/streams", async Task<IResult> (
    Stream body,
    HttpRequest request,
    IWebHostEnvironment environment,
    CancellationToken ct) =>
{
    var fileName = Path.GetFileName(request.Headers["X-File-Name"].ToString());
    if (string.IsNullOrWhiteSpace(fileName))
    {
        return Results.BadRequest("Заголовок X-File-Name обязателен.");
    }

    var directory = Path.Combine(environment.ContentRootPath, "uploads");
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, fileName);

    await using (var target = File.Create(path))
    {
        await body.CopyToAsync(target, ct);
    }

    return Results.Ok(new { file = fileName, size = new FileInfo(path).Length });
});
```

### Контроллер

Метод добавляется в тот же `UploadsController` (у него уже есть `environment`):

```csharp
[HttpPost("streams")]
[RequestSizeLimit(512L * 1024 * 1024)]
public async Task<IActionResult> UploadStream(CancellationToken ct)
{
    var fileName = Path.GetFileName(Request.Headers["X-File-Name"].ToString());
    if (string.IsNullOrWhiteSpace(fileName))
    {
        return BadRequest("Заголовок X-File-Name обязателен.");
    }

    var directory = Path.Combine(environment.ContentRootPath, "uploads");
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, fileName);

    await using (var target = System.IO.File.Create(path))
    {
        await Request.Body.CopyToAsync(target, ct);
    }

    return Ok(new { file = fileName, size = new FileInfo(path).Length });
}
```

### Что важно

- `Stream body` в Minimal API — это `HttpRequest.Body`, поток нельзя перечитать.
- Если включить повторы для POST (`RetryNonIdempotentMethods: true`), обработчик библиотеки
  прочитает тело в `byte[]` ради возможности повтора — для больших файлов это нежелательно.
  Оставьте повторы для POST выключенными (значение по умолчанию).
- Проверка размера на сервере: `Request.ContentLength`, `MaxRequestBodySize` в Kestrel или
  `[RequestSizeLimit]`.

---

## 8. Поток: скачивание файла

### Клиент

Ниже — сохранение на диск сервера/консоли. В браузере поток передаётся в JS через
`DotNetStreamReference` или `IBrowserFile`-подобный interop; `File.Create` там недоступен.

```csharp
using var response = await http
    .NewRequest($"api/cases/files/{id}")
    // Заголовки приходят сразу, тело читается потоком.
    .WithCompletionOption(HttpCompletionOption.ResponseHeadersRead)
    .SendAsync(ct);

response.EnsureSuccessStatusCode();

await using var source = await response.Content.ReadAsStreamAsync(ct);
await using var target = File.Create($"download-{id}.bin");
await source.CopyToAsync(target, ct);
```

Если сервер отдаёт не файл, а JSON-описание, используйте `SendJsonAsync<T>` как в остальных кейсах.

### Minimal API

```csharp
app.MapGet("/api/cases/files/{id:guid}", IResult (Guid id, IWebHostEnvironment environment) =>
{
    var path = Path.Combine(environment.ContentRootPath, "uploads", $"{id:N}.bin");

    return File.Exists(path)
        ? Results.File(path, "application/octet-stream", $"{id}.bin", enableRangeProcessing: true)
        : Results.NotFound();
});
```

### Контроллер

```csharp
[ApiController]
[Route("api/cases")]
public sealed class FilesController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("files/{id:guid}")]
    [Produces("application/octet-stream")]
    public IActionResult Download(Guid id)
    {
        var path = Path.Combine(environment.ContentRootPath, "uploads", $"{id:N}.bin");

        return System.IO.File.Exists(path)
            ? File(
                System.IO.File.OpenRead(path),
                "application/octet-stream",
                $"{id}.bin",
                enableRangeProcessing: true)
            : NotFound();
    }
}
```

### Что важно

- `ResponseHeadersRead` не буферизует тело: память не зависит от размера файла.
- Ответ нужно освободить (`using`), иначе соединение останется занятым.
- `enableRangeProcessing: true` включает `Range`/`206 Partial Content` — полезно для докачки
  и медиа.
- Общий `Timeout` клиента действует и на чтение тела. Для долгих скачиваний увеличьте
  `Timeout` или поставьте `00:00:00` (без ограничения).

---

## 9. Сырой JSON и произвольный текст

### Клиент

```csharp
// Готовая строка JSON — без повторной сериализации.
using var raw = await http
    .NewRequest("api/cases/echo-json", HttpMethod.Post)
    .WithJsonContent("""{"name":"raw","price":1.5}""")
    .SendAsync(ct);

// CSV с явным медиатипом.
using var csv = await http
    .NewRequest("api/cases/import", HttpMethod.Post)
    .WithStringContent("name,price\nОтчёт,199.90", "text/csv")
    .SendAsync(ct);
```

### Minimal API

```csharp
app.MapPost("/api/cases/echo-json", IResult (JsonElement payload) => Results.Ok(payload));

app.MapPost("/api/cases/import", async Task<IResult> (HttpRequest request, CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    var text = await reader.ReadToEndAsync(ct);
    return Results.Ok(new { length = text.Length });
});
```

### Контроллер

Методы добавляются в `CasesController`:

```csharp
[HttpPost("echo-json")]
public ActionResult<JsonElement> Echo([FromBody] JsonElement payload) => Ok(payload);

[HttpPost("import")]
[Consumes("text/csv")]
public async Task<IActionResult> Import(CancellationToken ct)
{
    using var reader = new StreamReader(Request.Body);
    var text = await reader.ReadToEndAsync(ct);
    return Ok(new { length = text.Length });
}
```

### Что важно

- `[FromBody] string` для `text/plain` не сработает без своего `InputFormatter` — читайте
  `Request.Body` (или принимайте `Stream`, см. кейс 7).
- JSON-формат `application/problem+json` и другие `*+json` сервер принимает как JSON.
- Для произвольного текста важен `Content-Type`: `WithStringContent` по умолчанию ставит
  `text/plain; charset=utf-8`, второй аргумент задаёт свой.

---

## 10. Ошибки: ProblemDetails

### Сервер

Minimal API:

```csharp
app.MapPost("/api/cases/items/validate", IResult (CreateItemRequest request) =>
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        errors["name"] = ["Имя обязательно."];
    }

    if (request.Price <= 0)
    {
        errors["price"] = ["Цена должна быть больше нуля."];
    }

    return errors.Count > 0
        ? Results.ValidationProblem(errors)
        : Results.Ok(request);
});
```

Контроллер — автоматически, если модель не прошла `DataAnnotations`:

```csharp
public sealed class CreateItemRequestWithValidation
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 1_000_000)]
    public decimal Price { get; set; }
}
```

Ответ `400`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "name": ["Имя обязательно."],
    "price": ["Цена должна быть больше нуля."]
  },
  "traceId": "00-...-01"
}
```

### Клиент

Вариант 1 — типизированный успешный ответ и `EnsureSuccess`:

```csharp
try
{
    var item = (await http
        .NewRequest("api/cases/items", HttpMethod.Post)
        .WithJsonContent(request)
        .SendJsonAsync<ItemDto>(ct))
        .EnsureSuccess()
        .Content!;
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
{
    // ex.Message содержит код и начало тела ответа.
    logger.LogWarning("Сервер отклонил запрос: {Message}", ex.Message);
}
```

Вариант 2 — разобрать `ProblemDetails` вручную:

```csharp
using var response = await http
    .NewRequest("api/cases/items", HttpMethod.Post)
    .WithJsonContent(request)
    .SendAsync(ct);

if (!response.IsSuccessStatusCode)
{
    var problem = await response.Content.ReadFromJsonAsync<ProblemDto>(ct);

    logger.LogWarning(
        "Ошибка {Status}: {Title}; поля: {Fields}",
        (int)response.StatusCode,
        problem?.Title,
        problem?.Errors is null ? "нет" : string.Join(", ", problem.Errors.Keys));

    return;
}

var item = await response.Content.ReadFromJsonAsync<ItemDto>(ct);
```

### Что важно

- `SendJsonAsync` не бросает исключение на HTTP-код ошибки: `StatusCode`, `RawContent`
  и заголовки доступны. Исключение — только по `EnsureSuccess()`.
- Если тело ошибки не JSON (HTML-страница прокси), `SendJsonAsync` не упадёт с `JsonException`:
  `Content` будет `null`, тело останется в `RawContent`. Для успешного ответа невалидный
  JSON по-прежнему даёт `JsonException`.
- Тип `ProblemDto` без `Errors` можно не указывать — лишние поля JSON игнорируются.

---

## 11. Повтор POST и Idempotency-Key

По умолчанию библиотека повторяет только идемпотентные методы: повтор POST может создать
дубликат. Если сервер это поддерживает, включите повторы и передавайте ключ идемпотентности.

### Клиент

`appsettings.json`:

```json
{
  "HttpClients": {
    "CasesApi": {
      "BaseAddress": "https://localhost:7150/",
      "Resilience": {
        "Retry": {
          "MaxRetryAttempts": 2,
          "RetryNonIdempotentMethods": true
        }
      }
    }
  }
}
```

```csharp
var idempotencyKey = Guid.NewGuid().ToString("N");

var item = await http
    .NewRequest("api/cases/items-with-key", HttpMethod.Post)
    .WithHeader("Idempotency-Key", idempotencyKey)
    .WithJsonContent(request)
    .SendJsonAsync<ItemDto>(ct);
```

Обработчик повторяет запрос с тем же заголовком: сервер увидит один и тот же ключ.

### Minimal API

```csharp
var itemsByKey = new ConcurrentDictionary<string, ItemDto>(StringComparer.Ordinal);

app.MapPost("/api/cases/items-with-key", IResult (HttpRequest request, CreateItemRequest body) =>
{
    var key = request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.BadRequest("Заголовок Idempotency-Key обязателен.");
    }

    var item = itemsByKey.GetOrAdd(key, _ => new ItemDto(
        Guid.NewGuid(),
        body.Name,
        body.Price,
        body.Quantity,
        DateTimeOffset.UtcNow));

    return Results.Ok(item);
});
```

### Контроллер

Статическое поле и метод добавляются в `CasesController`:

```csharp
private static readonly ConcurrentDictionary<string, ItemDto> ItemsByKey = new(StringComparer.Ordinal);

[HttpPost("items-with-key")]
public ActionResult<ItemDto> CreateWithKey(
    [FromHeader(Name = "Idempotency-Key")] string? key,
    [FromBody] CreateItemRequest request)
{
    if (string.IsNullOrWhiteSpace(key))
    {
        return BadRequest("Заголовок Idempotency-Key обязателен.");
    }

    var item = ItemsByKey.GetOrAdd(key, _ => new ItemDto(
        Guid.NewGuid(),
        request.Name,
        request.Price,
        request.Quantity,
        DateTimeOffset.UtcNow));

    return Ok(item);
}
```

### Что важно

- Ключ должен быть одинаковым для всех попыток — не генерируйте его внутри обработчика
  повторов; передавайте один раз в `WithHeader`.
- В реальном сервере ключ хранится в БД/Redis с TTL и результатом первой операции.
- `RetryNonIdempotentMethods` включайте только для таких идемпотентных операций.

---

## 12. Авторизация и отмена

### Клиент

```csharp
using var response = await http
    .NewRequest("api/cases/me")
    .WithBearerToken(accessToken)
    .SendAsync(ct);
```

### Minimal API

```csharp
app.MapGet("/api/cases/me", IResult (ClaimsPrincipal user) => Results.Ok(new { name = user.Identity?.Name }))
   .RequireAuthorization();
```

### Контроллер

Методы добавляются в `CasesController`:

```csharp
[Authorize]
[HttpGet("me")]
public ActionResult<object> Me() => Ok(new { name = User.Identity?.Name });
```

Отмена передаётся в серверный код через `CancellationToken`:

```csharp
app.MapGet("/api/cases/slow", async Task<IResult> (CancellationToken ct) =>
{
    await Task.Delay(TimeSpan.FromSeconds(5), ct);
    return Results.Ok();
});
```

В контроллере — параметр `CancellationToken` (метод добавляется в `CasesController`):

```csharp
[HttpGet("slow")]
public async Task<IActionResult> Slow(CancellationToken ct)
{
    await Task.Delay(TimeSpan.FromSeconds(5), ct);
    return Ok();
}
```

Клиент отменяет запрос тем же токеном:

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
await http.NewRequest("api/cases/slow").SendAsync(cancellation.Token);
// OperationCanceledException — отмена вызывающим кодом (не путать с таймаутом клиента).
```

---

## 13. Сводная таблица

| Что отправляет клиент | Код клиента | Как принимает Minimal API | Как принимает контроллер |
|---|---|---|---|
| JSON-объект | `WithJsonContent(dto)` + `SendJsonAsync<T>` | параметр-объект (body) | `[FromBody]` |
| JSON source-gen | `WithJsonContent(dto, typeInfo)` | то же | то же |
| Query | `WithQuery(name, value)` | `[FromQuery]` | `[FromQuery]` |
| Массив в query | `WithQuery(name, new[] { ... })` | `string[]`/`int[]` | `string[]`/`int[]` |
| Флаг в query | `WithQuery("debug")` | `bool debug` / `string?` | `bool`/`string?` |
| URL-encoded форма | `WithFormUrlEncodedContent(...)` | `[FromForm]` + `.DisableAntiforgery()` | `[FromForm]` |
| Multipart (поля+файл) | `WithMultipartFormData(...)` | `[FromForm]` + `.DisableAntiforgery()` | `[FromForm]` + `IFormFile` |
| Поток вверх | `WithStreamContent(stream)` | `Stream body` | `Request.Body` |
| Поток вниз | `WithCompletionOption(ResponseHeadersRead)` | `Results.File(...)` | `File(stream, ...)` |
| Готовый JSON | `WithJsonContent(json)` | `JsonElement` | `[FromBody] JsonElement` |
| CSV/текст | `WithStringContent(text, "text/csv")` | `Request.Body` + `StreamReader` | `Request.Body` + `StreamReader` |
| Заголовки | `WithHeader(name, value)` | `[FromHeader]` / `Request.Headers` | `[FromHeader]` / `Request.Headers` |
| Bearer | `WithBearerToken(token)` | `.RequireAuthorization()` | `[Authorize]` |

---

## 14. Чек-лист сервера

- [ ] **Minimal API + форма**: `.DisableAntiforgery()` только для API с Bearer/API-key;
      для браузерных форм с cookie оставьте токен и `app.UseAntiforgery()`.
- [ ] **MVC**: `AddControllers()` не включает antiforgery; при глобальном
      `AutoValidateAntiforgeryToken` добавьте `[IgnoreAntiforgeryToken]` на API-действия.
- [ ] **Размеры**: `MaxRequestBodySize` в Kestrel + `[RequestSizeLimit]`/`[RequestFormLimits]`
      для multipart; иначе большие файлы дадут `413`.
- [ ] **Типы**: `[ApiController]` сам отдаёт `400` `ValidationProblemDetails`; в Minimal API
      возвращайте `Results.ValidationProblem` вручную.
- [ ] **CORS** (для Blazor WASM): разрешите origin клиента, методы и заголовки
      (`Authorization`, `X-File-Name`, `Idempotency-Key`); чтобы прочитать нестандартный
      заголовок ответа в браузере, добавьте его в `Access-Control-Expose-Headers`.
- [ ] **Файлы**: проверяйте расширение/размер/имя, не доверяйте `IFormFile.FileName`
      (используйте `Path.GetFileName` и генерируйте своё имя).
- [ ] **Повторы**: POST не должен быть неидемпотентным, если клиент включает
      `RetryNonIdempotentMethods`; иначе используйте `Idempotency-Key`.

## 15. Чек-лист клиента

- [ ] **Адрес**: `BaseAddress` в конфигурации оканчивается на `/` (или трактуется как каталог);
      относительные пути без ведущего `/`.
- [ ] **Формы и файлы**: не включайте `RetryNonIdempotentMethods` для загрузок — тело
      не будет копироваться в память; для multipart не используйте поток после `SendAsync`.
- [ ] **Большие ответы**: `ResponseHeadersRead` + `using` + чтение потока.
- [ ] **Таймауты**: общий `Timeout` клиента действует на всю операцию, включая чтение тела;
      для больших файлов увеличьте его или поставьте `00:00:00`.
- [ ] **Ошибки**: для 4xx/5xx читайте `RawContent`/`ProblemDto`; `EnsureSuccess` бросает
      `HttpRequestException` с кодом.
- [ ] **AOT/тримминг**: используйте перегрузки с `JsonTypeInfo<T>` и `JsonSerializerContext`.
- [ ] **Browser**: `Authorization` и кастомные заголовки вызывают preflight — сервер должен
      разрешить их в CORS; токены не храните в `wwwroot/appsettings.json`.
