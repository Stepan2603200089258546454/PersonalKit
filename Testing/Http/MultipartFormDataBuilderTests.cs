using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;

namespace Common.Helper.Http.Tests.Http;

public sealed class MultipartFormDataBuilderTests
{
    [Fact]
    public async Task FieldAndFile_AreSent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("file-content"));

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form
                .AddField("title", "hello")
                .AddFile("document", stream, "report.txt", "text/plain"))
            .SendAsync();

        var contentType = handler.Requests[0].ContentHeaders["Content-Type"].Single();
        Assert.StartsWith("multipart/form-data; boundary=", contentType, StringComparison.Ordinal);

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("hello", body, StringComparison.Ordinal);
        Assert.Contains("file-content", body, StringComparison.Ordinal);
        Assert.Contains("report.txt", body, StringComparison.Ordinal);
        Assert.Contains("text/plain", body, StringComparison.Ordinal);
        Assert.Equal(2, CountParts(body));
    }

    [Fact]
    public async Task JsonPart_IsSerialized()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddJson("metadata", new { ItemId = 5, Name = "sample" }))
            .SendAsync();

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("\"itemId\":5", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"sample\"", body, StringComparison.Ordinal);
        Assert.Contains("application/json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BytesPart_WithFileName_IsSent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddBytes("file", Encoding.UTF8.GetBytes("binary-data"), "data.bin"))
            .SendAsync();

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("binary-data", body, StringComparison.Ordinal);
        Assert.Contains("data.bin", body, StringComparison.Ordinal);
        Assert.Contains("application/octet-stream", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamPart_WithoutFileName_IsSent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("stream-content"));

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddStream("payload", stream, "text/plain"))
            .SendAsync();

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("stream-content", body, StringComparison.Ordinal);
        Assert.DoesNotContain("filename=", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultipleParts_AreAllPresent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form
                .AddField("first", "1")
                .AddField("second", "2")
                .AddJson("third", new { Value = 3 }))
            .SendAsync();

        Assert.Equal(3, CountParts(handler.Requests[0].BodyText!));
    }

    [Fact]
    public async Task EmptyFieldValue_IsSent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddField("empty", null))
            .SendAsync();

        Assert.Equal(1, CountParts(handler.Requests[0].BodyText!));
    }

    [Fact]
    public void Validation_Throws()
    {
        var builder = new MultipartFormDataBuilder();

        Assert.ThrowsAny<ArgumentException>(() => builder.AddField("", "value"));
        Assert.ThrowsAny<ArgumentException>(() => builder.AddJson<object>(" ", new { }));
        Assert.Throws<ArgumentNullException>(() => builder.AddBytes("file", null!, "data.bin"));
        Assert.ThrowsAny<ArgumentException>(() => builder.AddBytes("file", [1], " "));
        Assert.Throws<ArgumentNullException>(() => builder.AddFile("file", null!, "report.txt"));
        Assert.ThrowsAny<ArgumentException>(() => builder.AddFile("file", new MemoryStream(), " "));
        Assert.Throws<ArgumentNullException>(() => builder.AddStream("payload", null!));
        Assert.ThrowsAny<ArgumentException>(() => builder.AddStream(" ", new MemoryStream()));
    }

    [Fact]
    public async Task JsonPart_UsesCustomSerializerOptions()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = null };

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddJson("metadata", new { ItemId = 5 }, options))
            .SendAsync();

        Assert.Contains("\"ItemId\":5", handler.Requests[0].BodyText!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonPart_WithJsonTypeInfo_UsesSourceGeneratedMetadata()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddJson(
                "metadata",
                new Common.Helper.Http.Tests.Models.ItemDto { Name = "typed", Count = 2 },
                Common.Helper.Http.Tests.Models.TestJsonContext.Default.ItemDto))
            .SendAsync();

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("\"name\":\"typed\"", body, StringComparison.Ordinal);
        Assert.Contains("\"count\":2", body, StringComparison.Ordinal);
    }

    private static int CountParts(string body) =>
        body.Split("Content-Disposition: form-data", StringSplitOptions.None).Length - 1;

    private static HttpClient CreateClient(RecordingHttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
}
