using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Common.Helper.Http;

/// <summary>
/// Описание одной части multipart-формы. Содержимое создаётся заново при каждой сборке формы,
/// поэтому форму из полей, JSON и байт можно отправлять повторно.
/// </summary>
internal sealed class MultipartPart
{
    private readonly Func<HttpContent> _contentFactory;

    private MultipartPart(string name, Func<HttpContent> contentFactory, string? fileName)
    {
        Name = name;
        _contentFactory = contentFactory;
        FileName = fileName;
    }

    public string Name { get; }

    public string? FileName { get; }

    public static MultipartPart Field(string name, string value) =>
        new(name, () => new StringContent(value, Encoding.UTF8), fileName: null);

    public static MultipartPart Json(string name, string json) =>
        new(name, () => new StringContent(json, Encoding.UTF8, "application/json"), fileName: null);

    public static MultipartPart Bytes(string name, byte[] content, string contentType, string? fileName) =>
        new(name, () => WithContentType(new ByteArrayContent(content), contentType), fileName);

    public static MultipartPart Stream(string name, Stream stream, string contentType, string? fileName) =>
        new(name, () => WithContentType(new StreamContent(stream), contentType), fileName);

    public HttpContent CreateContent() => _contentFactory();

    private static HttpContent WithContentType(HttpContent content, string contentType)
    {
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }
}
