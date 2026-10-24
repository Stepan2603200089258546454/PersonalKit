using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>
/// Подставной <see cref="HttpMessageHandler"/>, который записывает запросы и отдаёт ответы по сценарию.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public RecordingHttpMessageHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public static RecordingHttpMessageHandler Status(HttpStatusCode statusCode) =>
        new((_, _, _) => Task.FromResult(Responses.Status(statusCode)));

    public static RecordingHttpMessageHandler Sequence(params HttpStatusCode[] statusCodes) =>
        new((index, _, _) => Task.FromResult(Responses.Status(statusCodes[Math.Min(index, statusCodes.Length - 1)])));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contentHeaders = request.Content is null
            ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            : request.Content.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var recorded = new RecordedRequest
        {
            Method = request.Method.Method,
            RequestUri = request.RequestUri,
            Headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            ContentHeaders = contentHeaders,
            Body = body,
        };

        var index = Requests.Count;
        Requests.Add(recorded);

        return await _responder(index, request, cancellationToken).ConfigureAwait(false);
    }
}
