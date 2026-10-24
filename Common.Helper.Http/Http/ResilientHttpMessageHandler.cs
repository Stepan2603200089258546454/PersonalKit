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
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var timeout = options.Timeout;
        using var timeoutCts = timeout > TimeSpan.Zero ? new CancellationTokenSource(timeout) : null;
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var token = linkedCts?.Token ?? cancellationToken;

        _logger?.LogDebug(
            "HTTP client '{ClientName}': запрос {Method} {Uri}, таймаут {Timeout}.",
            _clientName,
            request.Method,
            requestUri,
            timeout > TimeSpan.Zero ? timeout : TimeSpan.MaxValue);

        try
        {
            var allowRetry = ResolveRetryAllowed(request.Method, options);
            var pipeline = _pipelines.GetPipeline(_clientName, options, allowRetry);

            return await pipeline.ExecuteAsync(
                ct => SendAttemptAsync(request, requestUri, body, options, ct),
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

    private async ValueTask<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        Uri requestUri,
        byte[]? body,
        ResilientHttpClientOptions options,
        CancellationToken cancellationToken)
    {
        var clone = CreateRequest(request, requestUri, body, options);
        return await base.SendAsync(clone, cancellationToken).ConfigureAwait(false);
    }

    private static bool ResolveRetryAllowed(HttpMethod method, ResilientHttpClientOptions options)
    {
        if (options.Resilience?.Retry is not { Enabled: true } retry || retry.MaxRetryAttempts <= 0)
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

    private static Uri ResolveRequestUri(HttpRequestMessage request, ResilientHttpClientOptions options)
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

    private static Uri GetBaseAddress(ResilientHttpClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            throw new InvalidOperationException(
                "Для запроса с относительным адресом не настроен BaseAddress HTTP-клиента.");
        }

        if (!Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException($"BaseAddress '{options.BaseAddress}' не является абсолютным URI.");
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

        if (body is not null)
        {
            var content = new ByteArrayContent(body);
            if (source.Content is not null)
            {
                foreach (var header in source.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            clone.Content = content;
        }

        var defaultHeaders = options.DefaultHeaders;
        if (defaultHeaders is not null)
        {
            foreach (var header in defaultHeaders)
            {
                if (!clone.Headers.Contains(header.Key))
                {
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }
}
