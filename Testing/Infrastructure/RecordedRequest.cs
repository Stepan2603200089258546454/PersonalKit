using System;
using System.Collections.Generic;
using System.Net;
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

    /// <summary>Экземпляр содержимого, который получил обработчик: позволяет проверить буферизацию тела.</summary>
    public HttpContent? Content { get; init; }

    public Version Version { get; init; } = HttpVersion.Version11;

    public HttpVersionPolicy VersionPolicy { get; init; } = HttpVersionPolicy.RequestVersionOrLower;

    /// <summary>Имена параметров запроса, переданных через <see cref="HttpRequestMessage.Options"/>.</summary>
    public IReadOnlyList<string> OptionKeys { get; init; } = [];

    public string? BodyText => Body is null ? null : Encoding.UTF8.GetString(Body);
}
