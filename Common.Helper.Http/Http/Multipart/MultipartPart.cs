using System.IO;

namespace Common.Helper.Http;

/// <summary>Описание одной части multipart-формы.</summary>
internal sealed class MultipartPart
{
    public MultipartPartKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Value { get; init; }

    public byte[]? Bytes { get; init; }

    public Stream? Stream { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }
}
