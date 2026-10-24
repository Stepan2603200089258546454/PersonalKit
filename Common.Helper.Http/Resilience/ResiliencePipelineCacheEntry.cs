using System.Net.Http;
using Polly;

namespace Common.Helper.Http;

/// <summary>
/// Запись кэша конвейеров: хранит последние параметры и собранный для них конвейер.
/// </summary>
internal sealed class ResiliencePipelineCacheEntry
{
    public object Gate { get; } = new();

    public ResilientHttpClientOptions? Options { get; set; }

    public ResiliencePipeline<HttpResponseMessage>? Pipeline { get; set; }
}
