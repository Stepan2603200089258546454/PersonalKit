using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;
using Common.Helper.Http.Tests.Models;

namespace Common.Helper.Http.Tests.Http;

public sealed class HttpRequestBuilderJsonTests
{
    [Fact]
    public async Task SendJsonAsync_DeserializesSuccessResponse()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """{"name":"value","count":2}""");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content);
        Assert.Equal("value", response.Content.Name);
        Assert.Equal(2, response.Content.Count);
        Assert.Equal("""{"name":"value","count":2}""", response.RawContent);
    }

    [Fact]
    public async Task SendJsonAsync_EmptyBody_ReturnsDefaultContent()
    {
        var handler = JsonHandler(HttpStatusCode.NoContent, string.Empty);
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items", HttpMethod.Delete).SendJsonAsync<ItemDto>();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Null(response.Content);
        Assert.True(string.IsNullOrEmpty(response.RawContent));
    }

    [Fact]
    public async Task SendJsonAsync_NonSuccess_ReturnsErrorPayload()
    {
        var handler = JsonHandler(HttpStatusCode.NotFound, """{"title":"Not found"}""");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items/404").SendJsonAsync<ProblemDto>();

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(response.Content);
        Assert.Equal("Not found", response.Content.Title);
    }

    [Fact]
    public async Task EnsureSuccess_ThrowsForNonSuccessResponse()
    {
        var handler = JsonHandler(HttpStatusCode.BadRequest, """{"title":"Bad request"}""");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ProblemDto>();

        var exception = Assert.Throws<HttpRequestException>(() => response.EnsureSuccess());
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("Bad request", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSuccess_ReturnsSameInstanceForSuccessResponse()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """{"name":"value"}""");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        Assert.Same(response, response.EnsureSuccess());
    }

    [Fact]
    public async Task SendJsonAsync_InvalidJson_ThrowsJsonException()
    {
        var handler = JsonHandler(HttpStatusCode.OK, "not a json");
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<JsonException>(() => client.NewRequest("api/items").SendJsonAsync<ItemDto>());
    }

    [Fact]
    public async Task SendJsonAsync_UsesCustomSerializerOptions()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """{"item_name":"snake"}""");
        using var client = CreateClient(handler);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        var response = await client.NewRequest("api/items")
            .WithJsonSerializerOptions(options)
            .SendJsonAsync<ItemDto>();

        Assert.NotNull(response.Content);
        Assert.Equal("snake", response.Content.ItemName);
    }

    [Fact]
    public async Task SendJsonAsync_ExposesResponseHeaders()
    {
        var handler = JsonHandler(
            HttpStatusCode.OK,
            """{"name":"value"}""",
            configure: response => response.Headers.TryAddWithoutValidation("X-Request-Id", "abc"));
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        Assert.Equal(["abc"], response.Headers.GetValues("X-Request-Id"));
    }

    [Fact]
    public async Task SendJsonAsync_SendsConfiguredContentAndMethod()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """{"name":"created"}""");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithJsonContent(new { Name = "new" })
            .SendJsonAsync<ItemDto>();

        Assert.Equal("POST", handler.Requests[0].Method);
        Assert.Contains("\"name\":\"new\"", handler.Requests[0].BodyText!, StringComparison.Ordinal);
        Assert.Equal("created", response.Content!.Name);
    }

    [Fact]
    public async Task WithJsonContent_WithJsonTypeInfo_UsesSourceGeneratedMetadata()
    {
        var handler = JsonHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithJsonContent(new ItemDto { Name = "typed", Count = 3 }, TestJsonContext.Default.ItemDto)
            .SendAsync();

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("\"name\":\"typed\"", body, StringComparison.Ordinal);
        Assert.Contains("\"count\":3", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendJsonAsync_WithJsonTypeInfo_UsesSourceGeneratedMetadata()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """{"name":"value","count":7}""");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items")
            .SendJsonAsync(TestJsonContext.Default.ItemDto);

        Assert.True(response.IsSuccessStatusCode);
        Assert.NotNull(response.Content);
        Assert.Equal("value", response.Content.Name);
        Assert.Equal(7, response.Content.Count);
    }

    [Fact]
    public void WithJsonContent_WithNullJsonTypeInfo_Throws()
    {
        var handler = JsonHandler(HttpStatusCode.OK, "{}");
        using var client = CreateClient(handler);
        var builder = new HttpRequestBuilder(client);

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithJsonContent(new ItemDto(), (System.Text.Json.Serialization.Metadata.JsonTypeInfo<ItemDto>)null!));
    }

    [Fact]
    public async Task SendJsonAsync_MalformedErrorBody_ReturnsEmptyContentWithRawText()
    {
        var handler = JsonHandler(HttpStatusCode.InternalServerError, "<html>server error</html>");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        Assert.False(response.IsSuccessStatusCode);
        Assert.Null(response.Content);
        Assert.Equal("<html>server error</html>", response.RawContent);
    }

    [Fact]
    public async Task EnsureSuccess_OnMalformedErrorBody_IncludesRawBody()
    {
        var handler = JsonHandler(HttpStatusCode.InternalServerError, "<html>server error</html>");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        var exception = Assert.Throws<HttpRequestException>(() => response.EnsureSuccess());
        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Contains("server error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendJsonAsync_MalformedErrorBody_WithJsonTypeInfo_ReturnsEmptyContent()
    {
        var handler = JsonHandler(HttpStatusCode.BadGateway, "not a json");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items")
            .SendJsonAsync(TestJsonContext.Default.ItemDto);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Null(response.Content);
        Assert.Equal("not a json", response.RawContent);
    }

    [Fact]
    public async Task SendJsonAsync_MalformedSuccessBody_WithJsonTypeInfo_ThrowsJsonException()
    {
        var handler = JsonHandler(HttpStatusCode.OK, "not a json");
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<JsonException>(() =>
            client.NewRequest("api/items").SendJsonAsync(TestJsonContext.Default.ItemDto));
    }

    [Fact]
    public async Task SendJsonAsync_JsonNullLiteral_ReturnsNullContent()
    {
        var handler = JsonHandler(HttpStatusCode.OK, "null");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Null(response.Content);
        Assert.Equal("null", response.RawContent);
    }

    [Fact]
    public async Task SendJsonAsync_WhitespaceBody_ReturnsDefaultContent()
    {
        var handler = JsonHandler(HttpStatusCode.OK, "   ");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        Assert.Null(response.Content);
        Assert.Equal("   ", response.RawContent);
    }

    [Fact]
    public async Task SendJsonAsync_NoContent_ReturnsNullContent()
    {
        var handler = JsonHandler(HttpStatusCode.NotFound, string.Empty);
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items/404").SendJsonAsync<ProblemDto>();

        Assert.False(response.IsSuccessStatusCode);
        Assert.Null(response.Content);
        Assert.True(string.IsNullOrEmpty(response.RawContent));
    }

    [Fact]
    public async Task SendJsonAsync_CyrillicBody_IsDecodedAsUtf8()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """{"name":"привет"}""");
        using var client = CreateClient(handler);

        var response = await client.NewRequest("api/items").SendJsonAsync<ItemDto>();

        Assert.Equal("привет", response.Content!.Name);
    }

    private static RecordingHttpMessageHandler JsonHandler(
        HttpStatusCode statusCode,
        string json,
        Action<HttpResponseMessage>? configure = null) =>
        new((_, _, _) =>
        {
            var response = new HttpResponseMessage(statusCode);
            if (json.Length > 0)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            configure?.Invoke(response);
            return Task.FromResult(response);
        });

    private static HttpClient CreateClient(RecordingHttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
}
