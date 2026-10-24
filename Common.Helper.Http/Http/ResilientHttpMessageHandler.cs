using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Common.Helper.Http;

/// <summary>
/// Обработчик, который перед каждой отправкой читает актуальные параметры клиента.
/// Благодаря этому горячая перезагрузка конфигурации меняет базовый адрес, заголовки,
/// таймауты и настройки устойчивости без перезапуска приложения.
/// </summary>
internal sealed class ResilientHttpMessageHandler : DelegatingHandler
{
    /// <summary>
    /// Подставной базовый адрес, который получает <see cref="HttpClient"/> при регистрации.
    /// Нужен, чтобы HttpClient принимал относительные адреса, а реальный базовый адрес
    /// подставлялся обработчиком при каждом запросе (и мог перезагружаться).
    /// </summary>
    internal const string DynamicBaseHost = "dynamic-http-client.invalid";

    internal const string DynamicBasePathPrefix = "/__dynamic_base__/";

    internal static readonly Uri DynamicBaseAddress = new($"https://{DynamicBaseHost}{DynamicBasePathPrefix}");

    private readonly string _clientName;
    private readonly IOptionsMonitor<ResilientHttpClientOptions> _options;
    private readonly ResiliencePipelineCache _pipelines;
    private readonly ILogger? _logger;

    /// <summary>
    /// Последний нормализованный базовый адрес: конфигурация меняется редко, а запросов много.
    /// Хранится парой «исходная строка → адрес», поэтому перезагрузка конфигурации
    /// не требует сброса кэша, а память не растёт от истории изменений.
    /// </summary>
    private Tuple<string, Uri>? _baseAddressCache;

    public ResilientHttpMessageHandler(
        string clientName,
        IOptionsMonitor<ResilientHttpClientOptions> options,
        ResiliencePipelineCache pipelines,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipelines);

        _clientName = clientName;
        _options = options;
        _pipelines = pipelines;
        _logger = loggerFactory?.CreateLogger<ResilientHttpMessageHandler>();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Get(_clientName);
        var requestUri = ResolveRequestUri(request, options);
        var allowRetry = ResolveRetryAllowed(request.Method, options);

        // Тело читается в память только если запрос может быть повторён.
        // Без повторов содержимое уходит в сеть как есть: загрузка файла в браузере
        // не требует лишней копии всего тела.
        var body = allowRetry && request.Content is not null
            ? await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var timeout = options.Timeout;
        using var timeoutCts = timeout > TimeSpan.Zero ? new CancellationTokenSource(timeout) : null;
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var token = linkedCts?.Token ?? cancellationToken;

        _logger?.LogDebug(
            "HTTP client '{ClientName}': запрос {Method} {Uri}, таймаут {Timeout}, повторы {RetryMode}.",
            _clientName,
            request.Method,
            requestUri,
            timeout > TimeSpan.Zero ? timeout : TimeSpan.MaxValue,
            allowRetry ? "включены" : "выключены");

        try
        {
            var pipeline = _pipelines.GetPipeline(_clientName, options, allowRetry);

            return await pipeline.ExecuteAsync(
                ct => SendAttemptAsync(request, requestUri, body, allowRetry, options, ct),
                token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException(
                $"Превышен общий таймаут HTTP-клиента '{_clientName}' ({timeout}).",
                new TimeoutException($"Общий таймаут HTTP-клиента '{_clientName}' истёк.", exception));
        }
    }

    private ValueTask<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        Uri requestUri,
        byte[]? body,
        bool allowRetry,
        ResilientHttpClientOptions options,
        CancellationToken cancellationToken)
    {
        var clone = CreateRequest(request, requestUri, body, allowRetry, options);
        return new ValueTask<HttpResponseMessage>(base.SendAsync(clone, cancellationToken));
    }

    private static bool ResolveRetryAllowed(HttpMethod method, ResilientHttpClientOptions options)
    {
        if (options.Resilience is not { Enabled: true } resilience
            || resilience.Retry is not { Enabled: true } retry
            || retry.MaxRetryAttempts <= 0)
        {
            return false;
        }

        if (retry.RetryNonIdempotentMethods)
        {
            return true;
        }

        // По умолчанию повторяются только идемпотентные методы: повтор POST/PATCH
        // может создать дубликат, что особенно заметно в браузере.
        return method.Method.ToUpperInvariant() is "GET" or "HEAD" or "OPTIONS" or "TRACE" or "PUT" or "DELETE";
    }

    private Uri ResolveRequestUri(HttpRequestMessage request, ResilientHttpClientOptions options)
    {
        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException("У запроса не задан RequestUri.");

        // Абсолютный адрес, не принадлежащий подставному базовому адресу, используется как есть.
        if (requestUri.IsAbsoluteUri && !IsDynamicBaseUri(requestUri))
        {
            return requestUri;
        }

        var baseAddress = GetBaseAddress(options);

        if (!requestUri.IsAbsoluteUri)
        {
            return new Uri(baseAddress, requestUri);
        }

        // HttpClient сам склеивает относительный адрес с подставным BaseAddress, поэтому здесь
        // возвращается исходная относительная часть. Так базовый адрес можно менять на лету.
        var pathAndQuery = requestUri.PathAndQuery;
        if (pathAndQuery.StartsWith(DynamicBasePathPrefix, StringComparison.Ordinal))
        {
            return new Uri(baseAddress, pathAndQuery.Substring(DynamicBasePathPrefix.Length));
        }

        return new Uri(baseAddress, pathAndQuery);
    }

    private static bool IsDynamicBaseUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, DynamicBaseHost, StringComparison.OrdinalIgnoreCase);

    private Uri GetBaseAddress(ResilientHttpClientOptions options)
    {
        var baseAddressText = options.BaseAddress;
        if (string.IsNullOrWhiteSpace(baseAddressText))
        {
            throw new InvalidOperationException(
                "Для запроса с относительным адресом не настроен BaseAddress HTTP-клиента.");
        }

        var cached = Volatile.Read(ref _baseAddressCache);
        if (cached is not null && string.Equals(cached.Item1, baseAddressText, StringComparison.Ordinal))
        {
            return cached.Item2;
        }

        var baseAddress = NormalizeBaseAddress(baseAddressText);
        Volatile.Write(ref _baseAddressCache, Tuple.Create(baseAddressText, baseAddress));
        return baseAddress;
    }

    private static Uri NormalizeBaseAddress(string baseAddressText)
    {
        if (!Uri.TryCreate(baseAddressText, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException($"BaseAddress '{baseAddressText}' не является абсолютным URI.");
        }

        // Базовый адрес без завершающего слэша трактуется как каталог:
        // "https://host/v1" и "https://host/v1/" дают одинаковый результат.
        if (!baseAddress.AbsolutePath.EndsWith('/'))
        {
            var builder = new UriBuilder(baseAddress) { Path = baseAddress.AbsolutePath + "/" };
            return builder.Uri;
        }

        return baseAddress;
    }

    private static HttpRequestMessage CreateRequest(
        HttpRequestMessage source,
        Uri requestUri,
        byte[]? body,
        bool allowRetry,
        ResilientHttpClientOptions options)
    {
        var clone = new HttpRequestMessage(source.Method, requestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in source.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (source.Content is not null)
        {
            // При одной попытке содержимое переиспользуется: копия тела не нужна.
            clone.Content = body is null
                ? source.Content
                : CreateBufferedContent(source.Content, body);
        }

        ApplyDefaultHeaders(clone, options);

        return clone;
    }

    private static ByteArrayContent CreateBufferedContent(HttpContent source, byte[] body)
    {
        var content = new ByteArrayContent(body);
        foreach (var header in source.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return content;
    }

    private static void ApplyDefaultHeaders(HttpRequestMessage request, ResilientHttpClientOptions options)
    {
        var defaultHeaders = options.DefaultHeaders;
        if (defaultHeaders is null)
        {
            return;
        }

        foreach (var header in defaultHeaders)
        {
            // Contains у HttpHeaders бросает исключение для заголовка «чужого» вида
            // (например, Content-Type в заголовках запроса), поэтому проверяем перебором.
            if (HasHeader(request.Headers, header.Key)
                || (request.Content is not null && HasHeader(request.Content.Headers, header.Key)))
            {
                continue;
            }

            // Заголовки содержимого (Content-Type и подобные) применяются к телу,
            // если оно есть: HttpClient не принимает их в Headers запроса.
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    private static bool HasHeader(System.Net.Http.Headers.HttpHeaders headers, string name)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
