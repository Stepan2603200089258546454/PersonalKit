namespace Common.Helper.Http;

/// <summary>Вид части multipart-формы.</summary>
internal enum MultipartPartKind
{
    /// <summary>Текстовое поле.</summary>
    Field,

    /// <summary>JSON-часть.</summary>
    Json,

    /// <summary>Часть из массива байт.</summary>
    Bytes,

    /// <summary>Часть из потока.</summary>
    Stream,
}
