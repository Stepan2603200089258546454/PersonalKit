using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Helper.Http;

/// <summary>
/// Fluent-построитель HTTP-запроса: адрес, метод, параметры строки запроса, заголовки,
/// содержимое (JSON, форма, текст, байты, поток, multipart) и отправка.
/// Запрос уходит через <see cref="HttpClient"/> клиента, поэтому к нему применяются
/// устойчивость и горячая перезагрузка параметров, настроенные в DI.
/// </summary>
/// <example>
/// <code>
/// using var response = await httpClient
///     .NewRequest("api/items")
///     .WithQuery("page", 2)
///     .WithHeader("X-Trace", "42")
///     .SendAsync(cancellationToken);
/// </code>
/// </example>
public sealed class HttpRequestBuilder
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly List<KeyValuePair<string, string?>> _query = [];
    private readonly List<KeyValuePair<string, string>> _headers = [];

    private string? _requestUri;
    private HttpMethod _method = HttpMethod.Get;
    private Func<HttpContent>? _contentFactory;
    private HttpCompletionOption _completionOption = HttpCompletionOption.ResponseContentRead;
    private JsonSerializerOptions _jsonOptions = DefaultJsonOptions;

    /// <summary>Создаёт построитель для указанного клиента. Адрес задаётся через <see cref="WithUri(string)"/>.</summary>
    /// <param name="client">HTTP-клиент, который отправит запрос.</param>
    public HttpRequestBuilder(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>Задаёт адрес запроса. Относительный адрес разрешается относительно BaseAddress клиента.</summary>
    /// <param name="requestUri">Абсолютный или относительный адрес.</param>
    public HttpRequestBuilder WithUri(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        _requestUri = requestUri;
        return this;
    }

    /// <summary>Задаёт адрес запроса.</summary>
    /// <param name="requestUri">Абсолютный или относительный адрес.</param>
    public HttpRequestBuilder WithUri(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        _requestUri = requestUri.OriginalString;
        return this;
    }

    /// <summary>Задаёт HTTP-метод. По умолчанию GET.</summary>
    /// <param name="method">HTTP-метод.</param>
    public HttpRequestBuilder WithMethod(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        _method = method;
        return this;
    }

    /// <summary>Добавляет параметр строки запроса. Значение приводится к инвариантной культуре.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="value">Значение; <c>null</c> даёт пустое значение.</param>
    public HttpRequestBuilder WithQuery(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _query.Add(new KeyValuePair<string, string?>(
            name,
            Convert.ToString(value, CultureInfo.InvariantCulture)));
        return this;
    }

    /// <summary>Добавляет несколько параметров строки запроса.</summary>
    /// <param name="parameters">Имена и значения параметров.</param>
    public HttpRequestBuilder WithQuery(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        foreach (var parameter in parameters)
        {
            WithQuery(parameter.Key, parameter.Value);
        }

        return this;
    }

    /// <summary>
    /// Добавляет заголовок запроса. Если заголовок с таким именем уже задан, он заменяется.
    /// Значение <c>null</c> удаляет заголовок.
    /// </summary>
    /// <param name="name">Имя заголовка.</param>
    /// <param name="value">Значение заголовка.</param>
    public HttpRequestBuilder WithHeader(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _headers.RemoveAll(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase));
        if (value is not null)
        {
            _headers.Add(new KeyValuePair<string, string>(name, value));
        }

        return this;
    }

    /// <summary>Добавляет несколько заголовков запроса.</summary>
    /// <param name="headers">Имена и значения заголовков.</param>
    public HttpRequestBuilder WithHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var header in headers)
        {
            WithHeader(header.Key, header.Value);
        }

        return this;
    }

    /// <summary>Задаёт заголовок <c>Accept</c>.</summary>
    /// <param name="mediaType">Медиатип, например <c>application/json</c>.</param>
    public HttpRequestBuilder WithAccept(string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return WithHeader("Accept", mediaType);
    }

    /// <summary>Добавляет заголовок <c>Authorization: Bearer</c>.</summary>
    /// <param name="token">Токен доступа.</param>
    public HttpRequestBuilder WithBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return WithHeader("Authorization", $"Bearer {token}");
    }

    /// <summary>Добавляет заголовок <c>Authorization: Basic</c>.</summary>
    /// <param name="userName">Имя пользователя.</param>
    /// <param name="password">Пароль.</param>
    public HttpRequestBuilder WithBasicAuthentication(string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(password);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}"));
        return WithHeader("Authorization", $"Basic {credentials}");
    }

    /// <summary>Задаёт параметры сериализации JSON для содержимого запроса и ответа.</summary>
    /// <param name="options">Параметры сериализации.</param>
    public HttpRequestBuilder WithJsonSerializerOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _jsonOptions = options;
        return this;
    }

    /// <summary>Отправляет значение как JSON. По умолчанию используются веб-настройки сериализации (camelCase).</summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="value">Значение для сериализации.</param>
    /// <param name="options">Необязательные параметры сериализации.</param>
    /// <remarks>
    /// Использует reflection-сериализатор. Для trimming/AOT используйте перегрузку
    /// с <see cref="JsonTypeInfo{T}"/>.
    /// </remarks>
    [RequiresUnreferencedCode("Сериализация JSON через reflection несовместима с полным триммингом и AOT. Используйте перегрузку с JsonTypeInfo<T>.")]
    [RequiresDynamicCode("Сериализация JSON через reflection может требовать генерации кода. Используйте перегрузку с JsonTypeInfo<T>.")]
    public HttpRequestBuilder WithJsonContent<T>(T value, JsonSerializerOptions? options = null)
    {
        var serializerOptions = options ?? _jsonOptions;
        return WithContentFactory(() => new StringContent(
            JsonSerializer.Serialize(value, serializerOptions),
            Encoding.UTF8,
            "application/json"));
    }

    /// <summary>
    /// Отправляет значение как JSON, используя метаданные сериализации из source-generated контекста.
    /// Безопасно для trimming и AOT.
    /// </summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="value">Значение для сериализации.</param>
    /// <param name="jsonTypeInfo">Метаданные типа из <see cref="JsonSerializerContext"/>.</param>
    public HttpRequestBuilder WithJsonContent<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        return WithContentFactory(() => new StringContent(
            JsonSerializer.Serialize(value, jsonTypeInfo),
            Encoding.UTF8,
            "application/json"));
    }

    /// <summary>Отправляет готовую строку JSON без повторной сериализации.</summary>
    /// <param name="json">Текст JSON.</param>
    /// <param name="mediaType">Медиатип содержимого.</param>
    public HttpRequestBuilder WithJsonContent(string json, string mediaType = "application/json")
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        return WithContentFactory(() => new StringContent(json, Encoding.UTF8, mediaType));
    }

    /// <summary>Отправляет данные как <c>application/x-www-form-urlencoded</c>.</summary>
    /// <param name="values">Имена и значения полей; <c>null</c> заменяется пустой строкой.</param>
    public HttpRequestBuilder WithFormUrlEncodedContent(IEnumerable<KeyValuePair<string, string?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var pairs = values
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value ?? string.Empty))
            .ToArray();

        return WithContentFactory(() => new FormUrlEncodedContent(pairs));
    }

    /// <summary>Отправляет текст с указанным медиатипом.</summary>
    /// <param name="content">Текст.</param>
    /// <param name="mediaType">Медиатип, по умолчанию <c>text/plain</c>.</param>
    /// <param name="encoding">Кодировка, по умолчанию UTF-8.</param>
    public HttpRequestBuilder WithStringContent(
        string content,
        string mediaType = "text/plain",
        Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        var contentEncoding = encoding ?? Encoding.UTF8;
        return WithContentFactory(() => new StringContent(content, contentEncoding, mediaType));
    }

    /// <summary>Отправляет массив байт.</summary>
    /// <param name="content">Содержимое.</param>
    /// <param name="mediaType">Медиатип, по умолчанию <c>application/octet-stream</c>.</param>
    public HttpRequestBuilder WithByteArrayContent(byte[] content, string mediaType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        return WithContentFactory(() =>
        {
            var byteArrayContent = new ByteArrayContent(content);
            byteArrayContent.Headers.ContentType = new(mediaType);
            return byteArrayContent;
        });
    }

    /// <summary>
    /// Отправляет поток. Поток читается при отправке; повторная отправка того же потока
    /// без перемотки невозможна.
    /// </summary>
    /// <param name="stream">Поток с содержимым.</param>
    /// <param name="mediaType">Медиатип, по умолчанию <c>application/octet-stream</c>.</param>
    public HttpRequestBuilder WithStreamContent(Stream stream, string mediaType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        return WithContentFactory(() =>
        {
            var streamContent = new StreamContent(stream);
            streamContent.Headers.ContentType = new(mediaType);
            return streamContent;
        });
    }

    /// <summary>
    /// Отправляет данные как <c>multipart/form-data</c>: поля, файлы, потоки, байты и JSON-части.
    /// </summary>
    /// <param name="configure">Настройка частей формы.</param>
    public HttpRequestBuilder WithMultipartFormData(Action<MultipartFormDataBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new MultipartFormDataBuilder();
        configure(builder);

        return WithContentFactory(builder.Build);
    }

    /// <summary>
    /// Использует готовое содержимое запроса. Экземпляр содержимого переиспользуется при отправке,
    /// поэтому повторная отправка возможна только для повторно читаемого содержимого.
    /// </summary>
    /// <param name="content">Содержимое запроса.</param>
    public HttpRequestBuilder WithContent(HttpContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _contentFactory = () => content;
        return this;
    }

    /// <summary>Убирает ранее заданное содержимое запроса.</summary>
    public HttpRequestBuilder WithoutContent()
    {
        _contentFactory = null;
        return this;
    }

    /// <summary>Задаёт способ чтения ответа. По умолчанию <see cref="HttpCompletionOption.ResponseContentRead"/>.</summary>
    /// <param name="completionOption">Способ чтения ответа.</param>
    public HttpRequestBuilder WithCompletionOption(HttpCompletionOption completionOption)
    {
        _completionOption = completionOption;
        return this;
    }

    /// <summary>
    /// Отправляет запрос и возвращает необработанный ответ. Ответ необходимо освободить.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest();
        return await _client.SendAsync(request, _completionOption, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет запрос, читает тело как JSON и возвращает результат с кодом ответа,
    /// заголовками, моделью и исходным текстом. Ошибки HTTP-кода не бросают исключение —
    /// для проверки используйте <see cref="ApiResponse{T}.EnsureSuccess"/>.
    /// </summary>
    /// <typeparam name="T">Тип модели ответа.</typeparam>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <remarks>
    /// Использует reflection-десериализатор. Для trimming/AOT используйте перегрузку
    /// с <see cref="JsonTypeInfo{T}"/>.
    /// </remarks>
    [RequiresUnreferencedCode("Десериализация JSON через reflection несовместима с полным триммингом и AOT. Используйте перегрузку с JsonTypeInfo<T>.")]
    [RequiresDynamicCode("Десериализация JSON через reflection может требовать генерации кода. Используйте перегрузку с JsonTypeInfo<T>.")]
    public Task<ApiResponse<T>> SendJsonAsync<T>(CancellationToken cancellationToken = default) =>
        SendJsonAsyncCore<T>(json => JsonSerializer.Deserialize<T>(json, _jsonOptions), cancellationToken);

    /// <summary>
    /// Отправляет запрос и читает тело как JSON, используя метаданные сериализации из
    /// source-generated контекста. Безопасно для trimming и AOT.
    /// </summary>
    /// <typeparam name="T">Тип модели ответа.</typeparam>
    /// <param name="jsonTypeInfo">Метаданные типа из <see cref="JsonSerializerContext"/>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task<ApiResponse<T>> SendJsonAsync<T>(
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return SendJsonAsyncCore(json => JsonSerializer.Deserialize(json, jsonTypeInfo), cancellationToken);
    }

    private async Task<ApiResponse<T>> SendJsonAsyncCore<T>(
        Func<string, T?> deserialize,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(cancellationToken).ConfigureAwait(false);

        var rawContent = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        T? content = default;
        if (!string.IsNullOrWhiteSpace(rawContent))
        {
            content = deserialize(rawContent);
        }

        return new ApiResponse<T>(response.StatusCode, response.Headers, content, rawContent);
    }

    private HttpRequestBuilder WithContentFactory(Func<HttpContent> contentFactory)
    {
        _contentFactory = contentFactory;
        return this;
    }

    private HttpRequestMessage BuildRequest()
    {
        if (string.IsNullOrWhiteSpace(_requestUri))
        {
            throw new InvalidOperationException("Не задан адрес запроса: вызовите WithUri перед отправкой.");
        }

        var request = new HttpRequestMessage(_method, BuildRequestUri());

        foreach (var header in _headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (_contentFactory is not null)
        {
            request.Content = _contentFactory();
        }

        return request;
    }

    private string BuildRequestUri()
    {
        if (_query.Count == 0)
        {
            return _requestUri!;
        }

        var separator = _requestUri!.Contains('?') ? '&' : '?';
        var queryString = string.Join(
            '&',
            _query.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value ?? string.Empty)}"));

        return _requestUri + separator + queryString;
    }
}
