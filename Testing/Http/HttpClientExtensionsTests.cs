using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;

namespace Common.Helper.Http.Tests.Http;

public sealed class HttpClientExtensionsTests
{
    [Fact]
    public async Task NewRequest_WithStringUri_CreatesRequest()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items").SendAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://example.test/api/items", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task NewRequest_WithUri_CreatesRequest()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest(new Uri("https://other.test/items")).SendAsync();

        Assert.Equal("https://other.test/items", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task NewRequest_WithMethod_UsesMethod()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest(HttpMethod.Delete, "api/items/1").SendAsync();

        Assert.Equal("DELETE", handler.Requests[0].Method);
    }

    [Fact]
    public void NewRequest_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HttpClientExtensions.NewRequest(null!, "api/items"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NewRequest_EmptyUri_Throws(string? requestUri)
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        Assert.ThrowsAny<ArgumentException>(() => client.NewRequest(requestUri!));
    }

    [Fact]
    public void NewRequest_NullUri_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        Assert.Throws<ArgumentNullException>(() => client.NewRequest((Uri)null!));
    }

    [Fact]
    public void NewRequest_NullMethod_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        Assert.Throws<ArgumentNullException>(() => client.NewRequest(null!, "api/items"));
    }

    private static HttpClient CreateClient(RecordingHttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
}
