using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Запись одного запроса, полученного подставным обработчиком.</summary>
internal sealed class RecordedRequest
{
    public required string Method { get; init; }

    public required Uri? RequestUri { get; init; }

    public required IReadOnlyDictionary<string, string[]> Headers { get; init; }

    public required IReadOnlyDictionary<string, string[]> ContentHeaders { get; init; }

    public required byte[]? Body { get; init; }

    public string? BodyText => Body is null ? null : Encoding.UTF8.GetString(Body);
}
