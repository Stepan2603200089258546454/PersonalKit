using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;

namespace Common.Helper.Http.Tests.Http;

public sealed class HttpRequestBuilderQueryTests
{
    [Fact]
    public async Task Boolean_IsLowercase()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("enabled", true)
            .WithQuery("deleted", false)
            .SendAsync();

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("enabled=true", query, StringComparison.Ordinal);
        Assert.Contains("deleted=false", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DateTime_IsIso8601()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var value = new DateTime(2026, 10, 24, 14, 5, 7, DateTimeKind.Utc);

        using var response = await client.NewRequest("api/items")
            .WithQuery("since", value)
            .SendAsync();

        Assert.Equal("2026-10-24T14:05:07Z", QueryValue(handler.Requests[0].RequestUri!, "since"));
    }

    [Fact]
    public async Task DateTimeOffset_IncludesOffset()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var value = new DateTimeOffset(2026, 10, 24, 14, 5, 7, TimeSpan.FromHours(3));

        using var response = await client.NewRequest("api/items")
            .WithQuery("since", value)
            .SendAsync();

        Assert.Equal("2026-10-24T14:05:07+03:00", QueryValue(handler.Requests[0].RequestUri!, "since"));
    }

    [Fact]
    public async Task DateOnly_IsIsoDate()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("day", new DateOnly(2026, 10, 24))
            .SendAsync();

        Assert.Equal("2026-10-24", QueryValue(handler.Requests[0].RequestUri!, "day"));
    }

    [Fact]
    public async Task TimeOnly_OmitsZeroFraction()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("at", new TimeOnly(14, 5, 7))
            .SendAsync();

        Assert.Equal("14:05:07", QueryValue(handler.Requests[0].RequestUri!, "at"));
    }

    [Fact]
    public async Task TimeOnly_KeepsFraction()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("at", new TimeOnly(14, 5, 7, 250))
            .SendAsync();

        Assert.Equal("14:05:07.25", QueryValue(handler.Requests[0].RequestUri!, "at"));
    }

    [Fact]
    public async Task Enum_UsesName()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("order", StringComparison.OrdinalIgnoreCase)
            .SendAsync();

        Assert.Equal("OrdinalIgnoreCase", QueryValue(handler.Requests[0].RequestUri!, "order"));
    }

    [Fact]
    public async Task Guid_UsesHyphenatedFormat()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var id = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        using var response = await client.NewRequest("api/items")
            .WithQuery("id", id)
            .SendAsync();

        Assert.Equal("6f9619ff-8b86-d011-b42d-00cf4fc964ff", QueryValue(handler.Requests[0].RequestUri!, "id"));
    }

    [Fact]
    public async Task Decimal_KeepsInvariantSeparator()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("price", 1.5m)
            .SendAsync();

        Assert.Equal("1.5", QueryValue(handler.Requests[0].RequestUri!, "price"));
    }

    [Fact]
    public async Task Collection_ExpandsToRepeatedParameters()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("id", new[] { 1, 2, 3 })
            .SendAsync();

        Assert.Equal("?id=1&id=2&id=3", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task EmptyCollection_AddsNothing()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("id", Array.Empty<int>())
            .SendAsync();

        Assert.Equal(string.Empty, handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task CollectionWithNull_AddsEmptyValue()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("tag", new[] { "a", null, "b" })
            .SendAsync();

        Assert.Equal("?tag=a&tag=&tag=b", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task String_IsSingleValue()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("tag", "a,b")
            .SendAsync();

        Assert.Equal("?tag=a%2Cb", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Flag_ProducesParameterWithoutValue()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("debug")
            .SendAsync();

        Assert.Equal("?debug", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Flag_CombinedWithValues_AndFragment()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items#section")
            .WithQuery("page", 2)
            .WithQuery("debug")
            .SendAsync();

        var uri = handler.Requests[0].RequestUri!;
        Assert.Equal("?page=2&debug", uri.Query);
        Assert.Equal("#section", uri.Fragment);
    }

    [Fact]
    public async Task NullValue_ProducesEmptyValue()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("filter", null)
            .SendAsync();

        Assert.Equal("?filter=", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task NameAndValue_AreEncoded()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items")
            .WithQuery("$filter", "a b&c=d+e")
            .SendAsync();

        var uri = handler.Requests[0].RequestUri!;
        Assert.StartsWith("?%24filter=", uri.Query, StringComparison.Ordinal);
        Assert.Equal("a b&c=d+e", QueryValue(uri, "$filter"));
        Assert.Single(uri.Query.TrimStart('?').Split('&'));
    }

    [Fact]
    public async Task ExistingQueryEndingWithAmpersand_NoDoubleSeparator()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);

        using var response = await client.NewRequest("api/items?existing=1&")
            .WithQuery("page", 2)
            .SendAsync();

        Assert.Equal("?existing=1&page=2", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public void EmptyName_Throws()
    {
        var handler = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var client = CreateClient(handler);
        var builder = new HttpRequestBuilder(client);

        Assert.ThrowsAny<ArgumentException>(() => builder.WithQuery(" ", 1));
        Assert.ThrowsAny<ArgumentException>(() => builder.WithQuery(" "));
        Assert.Throws<ArgumentNullException>(() => builder.WithQuery((IEnumerable<KeyValuePair<string, string?>>)null!));
        Assert.ThrowsAny<ArgumentException>(() =>
            builder.WithQuery(new Dictionary<string, string?> { [" "] = "value" }));
    }

    private static string QueryValue(Uri requestUri, string name)
    {
        foreach (var part in requestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? part : part[..separator]);
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                return separator < 0 ? string.Empty : Uri.UnescapeDataString(part[(separator + 1)..]);
            }
        }

        throw new InvalidOperationException($"Параметр '{name}' не найден в адресе '{requestUri}'.");
    }

    private static HttpClient CreateClient(RecordingHttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://example.test/"),
        };
}
