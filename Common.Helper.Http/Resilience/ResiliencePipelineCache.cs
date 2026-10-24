using System;
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Polly;

namespace Common.Helper.Http;

/// <summary>
/// Кэширует конвейеры устойчивости по имени клиента и пересобирает их при смене экземпляра параметров.
/// Пересборка выполняется при горячей перезагрузке конфигурации: <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// возвращает новый экземпляр параметров, что инвалидирует кэш.
/// </summary>
internal sealed class ResiliencePipelineCache
{
    private readonly ConcurrentDictionary<string, ResiliencePipelineCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ILogger? _logger;

    public ResiliencePipelineCache(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<ResiliencePipelineCache>();
    }

    public ResiliencePipeline<HttpResponseMessage> GetPipeline(
        string clientName,
        ResilientHttpClientOptions options,
        bool allowRetry = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientName);
        ArgumentNullException.ThrowIfNull(options);

        var cacheKey = allowRetry ? clientName : clientName + "|no-retry";
        var entry = _entries.GetOrAdd(cacheKey, static _ => new ResiliencePipelineCacheEntry());

        lock (entry.Gate)
        {
            if (entry.Pipeline is not null && ReferenceEquals(entry.Options, options))
            {
                return entry.Pipeline;
            }

            entry.Pipeline = ResiliencePipelineFactory.Create(options, clientName, allowRetry, _logger);
            entry.Options = options;
            return entry.Pipeline;
        }
    }
}
