using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>
/// Собирает обработчик устойчивости с подставным внутренним обработчиком
/// и даёт доступ к монитору параметров и кэшу конвейеров.
/// </summary>
internal sealed class ResilientHandlerHarness : IDisposable
{
    private readonly HttpMessageInvoker _invoker;

    public ResilientHandlerHarness(
        ResilientHttpClientOptions options,
        RecordingHttpMessageHandler inner,
        TestOptionsMonitor<ResilientHttpClientOptions>? monitor = null,
        ResiliencePipelineCache? cache = null,
        string clientName = "test-client")
    {
        Inner = inner;
        Monitor = monitor ?? new TestOptionsMonitor<ResilientHttpClientOptions>(options);
        Cache = cache ?? new ResiliencePipelineCache();
        ClientName = clientName;

        var handler = new ResilientHttpMessageHandler(ClientName, Monitor, Cache, NullLoggerFactory.Instance)
        {
            InnerHandler = inner,
        };

        _invoker = new HttpMessageInvoker(handler, disposeHandler: true);
    }

    public TestOptionsMonitor<ResilientHttpClientOptions> Monitor { get; }

    public ResiliencePipelineCache Cache { get; }

    public RecordingHttpMessageHandler Inner { get; }

    public string ClientName { get; }

    public ResiliencePipeline<HttpResponseMessage> Pipeline => Cache.GetPipeline(ClientName, Monitor.CurrentValue);

    public Task<HttpResponseMessage> SendAsync(
        string? requestUri = null,
        HttpMethod? method = null,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, requestUri);
        if (content is not null)
        {
            request.Content = content;
        }

        return _invoker.SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
        _invoker.SendAsync(request, cancellationToken);

    public void Dispose() => _invoker.Dispose();
}
