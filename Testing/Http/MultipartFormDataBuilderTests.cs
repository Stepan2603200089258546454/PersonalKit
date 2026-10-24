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
        Assert.Throws<ArgumentNullException>(() => builder.AddJson(
            "metadata",
            new { },
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>)null!));
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

    [Fact]
    public async Task BytesPart_DefaultContentTypeIsOctetStream()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddBytes("file", [1, 2, 3], "data.bin"))
            .SendAsync();

        Assert.Contains(
            "Content-Type: application/octet-stream",
            handler.Requests[0].BodyText!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FilePart_DefaultContentTypeIsOctetStream()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("file-content"));

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddFile("file", stream, "data.bin"))
            .SendAsync();

        Assert.Contains(
            "Content-Type: application/octet-stream",
            handler.Requests[0].BodyText!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FilePart_UsesCustomContentType()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddFile("document", stream, "data.json", "application/json"))
            .SendAsync();

        Assert.Contains(
            "Content-Type: application/json",
            handler.Requests[0].BodyText!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamPart_DefaultContentTypeIsOctetStream()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        using var stream = new MemoryStream([1, 2, 3]);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddStream("payload", stream))
            .SendAsync();

        Assert.Contains(
            "Content-Type: application/octet-stream",
            handler.Requests[0].BodyText!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonPart_ContentTypeIsJson()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddJson("metadata", new { Value = 1 }))
            .SendAsync();

        Assert.Contains(
            "Content-Type: application/json",
            handler.Requests[0].BodyText!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FieldPart_ContentTypeIsPlainText()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddField("title", "value"))
            .SendAsync();

        Assert.Contains(
            "Content-Type: text/plain",
            handler.Requests[0].BodyText!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnicodeField_IsSentAsUtf8()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddField("comment", "привет мир"))
            .SendAsync();

        Assert.Contains("привет мир", handler.Requests[0].BodyText!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_Twice_RebuildsNonStreamParts()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var builder = client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form
                .AddField("title", "hello")
                .AddJson("metadata", new { Value = 1 }));

        using var first = await builder.SendAsync();
        using var second = await builder.SendAsync();

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, CountParts(handler.Requests[0].BodyText!));
        Assert.Equal(2, CountParts(handler.Requests[1].BodyText!));
    }

    [Fact]
    public void Build_EmptyForm_ReturnsMultipartContent()
    {
        using var content = new MultipartFormDataBuilder().Build();

        Assert.Equal("multipart/form-data", content.Headers.ContentType!.MediaType);
        Assert.Empty(content);
    }

    [Fact]
    public async Task JsonPart_SerializesNestedCollections()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddJson("metadata", new
            {
                Tags = new[] { "a", "b" },
                Nested = new { Value = 1 },
            }))
            .SendAsync();

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("\"tags\":[\"a\",\"b\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"nested\":{\"value\":1}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BytesPart_EmptyContent_IsSent()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddBytes("file", [], "empty.bin"))
            .SendAsync();

        var body = handler.Requests[0].BodyText!;
        Assert.Contains("empty.bin", body, StringComparison.Ordinal);
        Assert.Equal(1, CountParts(body));
    }

    [Fact]
    public async Task StreamPart_UsesCustomContentType()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        using var stream = new MemoryStream([1, 2, 3]);

        using var response = await client.NewRequest("upload", HttpMethod.Post)
            .WithMultipartFormData(form => form.AddStream("image", stream, "image/png"))
            .SendAsync();

        Assert.Contains(
            "Content-Type: image/png",
            handler.Requests[0].BodyText!,
            StringComparison.OrdinalIgnoreCase);
    }

    private static int CountParts(string body) =>
        body.Split("Content-Disposition: form-data", StringSplitOptions.None).Length - 1;

    private static HttpClient CreateClient(RecordingHttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
}
