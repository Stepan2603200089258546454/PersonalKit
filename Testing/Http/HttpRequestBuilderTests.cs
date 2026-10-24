using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;

namespace Common.Helper.Http.Tests.Http;

public sealed class HttpRequestBuilderTests
{
    [Fact]
    public async Task DefaultMethod_IsGet()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items").SendAsync();

        Assert.Equal("GET", handler.Requests[0].Method);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task WithMethod_OverridesMethod(string method)
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithMethod(new HttpMethod(method))
            .SendAsync();

        Assert.Equal(method, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Send_WithoutUri_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new HttpRequestBuilder(client).SendAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void WithUri_EmptyUri_Throws(string? requestUri)
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        Assert.ThrowsAny<ArgumentException>(() => new HttpRequestBuilder(client).WithUri(requestUri!));
    }

    [Fact]
    public void WithUri_NullUri_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        Assert.Throws<ArgumentNullException>(() => new HttpRequestBuilder(client).WithUri((Uri)null!));
    }

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HttpRequestBuilder(null!));
    }

    [Fact]
    public async Task QueryParameters_AreAppendedAndEncoded()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("page", 2)
            .WithQuery("search", "привет мир")
            .SendAsync();

        var uri = handler.Requests[0].RequestUri!;
        Assert.Contains("page=2", uri.Query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("привет мир"), uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryParameters_ExistingQuery_UsesAmpersand()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items?existing=1")
            .WithQuery("page", 2)
            .SendAsync();

        Assert.Equal("?existing=1&page=2", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task QueryParameters_CanBeAddedFromCollection()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery(new Dictionary<string, string?>
            {
                ["page"] = "2",
                ["filter"] = null,
            })
            .SendAsync();

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("page=2", query, StringComparison.Ordinal);
        Assert.Contains("filter=", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryParameters_UseInvariantCulture()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("ratio", 1.5)
            .WithQuery("enabled", true)
            .SendAsync();

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("ratio=1.5", query, StringComparison.Ordinal);
        Assert.Contains("enabled=True", query, StringComparison.Ordinal);
    }

    [Fact]
    public void WithQuery_NullParameters_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        Assert.Throws<ArgumentNullException>(() =>
            new HttpRequestBuilder(client).WithQuery((IEnumerable<KeyValuePair<string, string?>>)null!));
    }

    [Fact]
    public async Task Headers_AreApplied()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithHeader("X-Trace", "42")
            .WithHeaders(new Dictionary<string, string> { ["X-Source"] = "tests" })
            .SendAsync();

        Assert.Equal(["42"], handler.Requests[0].Headers["X-Trace"]);
        Assert.Equal(["tests"], handler.Requests[0].Headers["X-Source"]);
    }

    [Fact]
    public async Task WithHeader_NullValue_RemovesHeader()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithHeader("X-Remove", "value")
            .WithHeader("X-Remove", null)
            .SendAsync();

        Assert.False(handler.Requests[0].Headers.ContainsKey("X-Remove"));
    }

    [Fact]
    public async Task WithAccept_AddsAcceptHeader()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithAccept("application/json")
            .SendAsync();

        Assert.Equal(["application/json"], handler.Requests[0].Headers["Accept"]);
    }

    [Fact]
    public async Task WithBearerToken_AddsAuthorizationHeader()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithBearerToken("secret")
            .SendAsync();

        Assert.Equal(["Bearer secret"], handler.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task WithBasicAuthentication_AddsAuthorizationHeader()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:password"));

        using var response = await client.NewRequest("api/items")
            .WithBasicAuthentication("user", "password")
            .SendAsync();

        Assert.Equal([$"Basic {expected}"], handler.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public void HeaderAndAuthValidation_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var builder = new HttpRequestBuilder(client);

        Assert.ThrowsAny<ArgumentException>(() => builder.WithHeader(" ", "value"));
        Assert.Throws<ArgumentNullException>(() => builder.WithHeaders(null!));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithAccept(""));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithBearerToken(" "));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithBasicAuthentication("", "password"));
        Assert.Throws<ArgumentNullException>(() => builder.WithBasicAuthentication("user", null!));
    }

    [Fact]
    public async Task JsonContent_SerializesWithWebDefaults()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithJsonContent(new { Name = "test", Value = 1 })
            .SendAsync();

        Assert.Contains("\"name\":\"test\"", handler.Requests[0].BodyText!, StringComparison.Ordinal);
        Assert.Contains("application/json", handler.Requests[0].ContentHeaders["Content-Type"].Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonContent_UsesCustomSerializerOptions()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = null };

        using var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithJsonSerializerOptions(options)
            .WithJsonContent(new { Name = "test" })
            .SendAsync();

        Assert.Contains("\"Name\":\"test\"", handler.Requests[0].BodyText!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonContent_RawJson_IsNotReserialized()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithJsonContent("{\"name\":\"raw\"}")
            .SendAsync();

        Assert.Equal("{\"name\":\"raw\"}", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task FormUrlEncodedContent_IsEncoded()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("login", HttpMethod.Post)
            .WithFormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["user"] = "john",
                ["comment"] = "hello world",
                ["empty"] = null,
            })
            .SendAsync();

        Assert.Equal("user=john&comment=hello+world&empty=", handler.Requests[0].BodyText);
        Assert.Contains("application/x-www-form-urlencoded", handler.Requests[0].ContentHeaders["Content-Type"].Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StringContent_UsesMediaType()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/report", HttpMethod.Post)
            .WithStringContent("a;b", "text/csv")
            .SendAsync();

        Assert.Equal("a;b", handler.Requests[0].BodyText);
        Assert.Contains("text/csv", handler.Requests[0].ContentHeaders["Content-Type"].Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ByteArrayContent_IsSent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var bytes = new byte[] { 1, 2, 3, 4 };

        using var response = await client.NewRequest("api/upload", HttpMethod.Post)
            .WithByteArrayContent(bytes, "application/octet-stream")
            .SendAsync();

        Assert.Equal(bytes, handler.Requests[0].Body);
        Assert.Contains("application/octet-stream", handler.Requests[0].ContentHeaders["Content-Type"].Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamContent_IsSent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("stream data"));

        using var response = await client.NewRequest("api/upload", HttpMethod.Post)
            .WithStreamContent(stream, "application/octet-stream")
            .SendAsync();

        Assert.Equal("stream data", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task WithContent_UsesProvidedContent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var content = new StringContent("direct", Encoding.UTF8, "text/plain");
        using var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithContent(content)
            .SendAsync();

        Assert.Equal("direct", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task WithoutContent_RemovesContent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithStringContent("text")
            .WithoutContent()
            .SendAsync();

        Assert.Null(handler.Requests[0].Body);
    }

    [Fact]
    public async Task LastContentWins()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items", HttpMethod.Post)
            .WithJsonContent(new { Name = "json" })
            .WithStringContent("plain text")
            .SendAsync();

        Assert.Equal("plain text", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task SendAsync_CanBeRepeatedForRecreatableContent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var builder = client.NewRequest("api/items", HttpMethod.Post).WithJsonContent(new { Name = "test" });

        using var first = await builder.SendAsync();
        using var second = await builder.SendAsync();

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(handler.Requests[0].BodyText, handler.Requests[1].BodyText);
    }

    [Fact]
    public void ContentValidation_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var builder = new HttpRequestBuilder(client);

        Assert.Throws<ArgumentNullException>(() => builder.WithJsonContent((string)null!));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithJsonContent("{}", " "));
        Assert.Throws<ArgumentNullException>(() => builder.WithFormUrlEncodedContent(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithStringContent(null!));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithStringContent("text", " "));
        Assert.Throws<ArgumentNullException>(() => builder.WithByteArrayContent(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithStreamContent(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithMultipartFormData(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithContent(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithJsonSerializerOptions(null!));
    }

    [Fact]
    public void WithCompletionOption_ReturnsBuilder()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        var builder = new HttpRequestBuilder(client).WithCompletionOption(HttpCompletionOption.ResponseHeadersRead);

        Assert.NotNull(builder);
    }

    private static HttpClient CreateClient(RecordingHttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
}
