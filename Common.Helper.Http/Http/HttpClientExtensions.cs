using System;
using System.Net.Http;

namespace Common.Helper.Http;

/// <summary>Точка входа в fluent-построение запроса.</summary>
public static class HttpClientExtensions
{
    /// <summary>Создаёт построитель запроса к указанному адресу.</summary>
    /// <param name="client">HTTP-клиент.</param>
    /// <param name="requestUri">Абсолютный или относительный адрес.</param>
    public static HttpRequestBuilder NewRequest(this HttpClient client, string requestUri)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        return new HttpRequestBuilder(client).WithUri(requestUri);
    }

    /// <summary>Создаёт построитель запроса к указанному адресу.</summary>
    /// <param name="client">HTTP-клиент.</param>
    /// <param name="requestUri">Абсолютный или относительный адрес.</param>
    public static HttpRequestBuilder NewRequest(this HttpClient client, Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestUri);

        return new HttpRequestBuilder(client).WithUri(requestUri);
    }

    /// <summary>Создаёт построитель запроса с заданным HTTP-методом.</summary>
    /// <param name="client">HTTP-клиент.</param>
    /// <param name="method">HTTP-метод.</param>
    /// <param name="requestUri">Абсолютный или относительный адрес.</param>
    public static HttpRequestBuilder NewRequest(this HttpClient client, HttpMethod method, string requestUri)
    {
        ArgumentNullException.ThrowIfNull(method);
        return NewRequest(client, requestUri).WithMethod(method);
    }

    /// <summary>Создаёт построитель запроса с заданным HTTP-методом.</summary>
    /// <param name="client">HTTP-клиент.</param>
    /// <param name="requestUri">Абсолютный или относительный адрес.</param>
    /// <param name="method">HTTP-метод.</param>
    public static HttpRequestBuilder NewRequest(this HttpClient client, string requestUri, HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return NewRequest(client, requestUri).WithMethod(method);
    }
}
