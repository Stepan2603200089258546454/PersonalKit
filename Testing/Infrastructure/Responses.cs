using System.Net;
using System.Net.Http;
using System.Text;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Готовые ответы для подставного обработчика.</summary>
internal static class Responses
{
    public static HttpResponseMessage Status(HttpStatusCode statusCode) =>
        new(statusCode) { Content = new StringContent($"{{\"status\":{(int)statusCode}}}", Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Ok(string body = "ok") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
}
