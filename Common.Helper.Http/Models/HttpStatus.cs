using System.Net;

namespace Common.Helper.Http;

/// <summary>Проверка успешности кода ответа HTTP.</summary>
internal static class HttpStatus
{
    /// <summary>Код ответа в диапазоне 200–299.</summary>
    public static bool IsSuccess(HttpStatusCode statusCode) => (int)statusCode is >= 200 and <= 299;
}
