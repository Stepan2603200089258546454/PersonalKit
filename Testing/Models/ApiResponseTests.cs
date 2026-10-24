using System;
using System.Net;
using System.Net.Http;

namespace Common.Helper.Http.Tests.Models;

public sealed class ApiResponseTests
{
    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(199, false)]
    [InlineData(300, false)]
    [InlineData(400, false)]
    [InlineData(500, false)]
    public void IsSuccessStatusCode_DependsOnCode(int statusCode, bool expected)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)statusCode);
        var result = new ApiResponse<string>((HttpStatusCode)statusCode, response.Headers, "content", "raw");

        Assert.Equal(expected, result.IsSuccessStatusCode);
    }

    [Fact]
    public void Constructor_ExposesValues()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created);
        response.Headers.TryAddWithoutValidation("X-Id", "1");

        var result = new ApiResponse<string>(HttpStatusCode.Created, response.Headers, "content", "raw");

        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.Equal("content", result.Content);
        Assert.Equal("raw", result.RawContent);
        Assert.Equal(["1"], result.Headers.GetValues("X-Id"));
    }

    [Fact]
    public void Constructor_NullHeaders_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ApiResponse<string>(HttpStatusCode.OK, null!, "content", "raw"));
    }

    [Fact]
    public void EnsureSuccess_Success_ReturnsSameInstance()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var result = new ApiResponse<string>(HttpStatusCode.OK, response.Headers, "content", "raw");

        Assert.Same(result, result.EnsureSuccess());
    }

    [Fact]
    public void EnsureSuccess_Failure_ThrowsWithStatusCode()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var result = new ApiResponse<string>(HttpStatusCode.InternalServerError, response.Headers, null, "server error");

        var exception = Assert.Throws<HttpRequestException>(() => result.EnsureSuccess());

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Contains("server error", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSuccess_TruncatesLongBodyInMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway);
        var body = new string('x', 5000);
        var result = new ApiResponse<string>(HttpStatusCode.BadGateway, response.Headers, null, body);

        var exception = Assert.Throws<HttpRequestException>(() => result.EnsureSuccess());

        Assert.True(exception.Message.Length < 5000);
        Assert.Contains(new string('x', 100), exception.Message, StringComparison.Ordinal);
    }
}
