using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Common.Helper.Http;

/// <summary>
/// Результат отправки запроса через <see cref="HttpRequestBuilder"/>:
/// код ответа, заголовки, десериализованное содержимое и исходный текст ответа.
/// </summary>
/// <typeparam name="T">Тип содержимого ответа.</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>Создаёт результат запроса.</summary>
    public ApiResponse(HttpStatusCode statusCode, HttpResponseHeaders headers, T? content, string? rawContent)
    {
        ArgumentNullException.ThrowIfNull(headers);

        StatusCode = statusCode;
        Headers = headers;
        Content = content;
        RawContent = rawContent;
    }

    /// <summary>Код ответа HTTP.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Признак успешного ответа: код в диапазоне 200–299.</summary>
    public bool IsSuccessStatusCode => HttpStatus.IsSuccess(StatusCode);

    /// <summary>Заголовки ответа.</summary>
    public HttpResponseHeaders Headers { get; }

    /// <summary>Десериализованное содержимое ответа (для пустого тела — значение по умолчанию).</summary>
    public T? Content { get; }

    /// <summary>Исходный текст ответа.</summary>
    public string? RawContent { get; }

    /// <summary>
    /// Проверяет, что ответ успешный. При неуспешном коде бросает <see cref="HttpRequestException"/>
    /// с кодом ответа и началом тела.
    /// </summary>
    /// <returns>Этот же результат для продолжения цепочки.</returns>
    public ApiResponse<T> EnsureSuccess()
    {
        if (IsSuccessStatusCode)
        {
            return this;
        }

        var body = RawContent is { Length: > 0 }
            ? RawContent[..Math.Min(RawContent.Length, 2048)]
            : null;

        var message = $"HTTP-запрос завершился с кодом {(int)StatusCode} ({StatusCode}).";
        if (body is not null)
        {
            message += Environment.NewLine + body;
        }

        throw new HttpRequestException(message, inner: null, StatusCode);
    }
}
