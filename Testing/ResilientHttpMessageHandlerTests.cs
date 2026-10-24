using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace Common.Helper.Http.Tests;

public sealed class ResilientHttpMessageHandlerTests
{
    [Fact]
    public void Constructor_RejectsEmptyClientName()
    {
        var monitor = new TestOptionsMonitor<ResilientHttpClientOptions>(TestOptions.Fast());

        Assert.Throws<ArgumentException>(() =>
            new ResilientHttpMessageHandler("", monitor, new ResiliencePipelineCache()));
    }

    [Fact]
    public void Constructor_RejectsNullMonitor()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ResilientHttpMessageHandler("client", null!, new ResiliencePipelineCache()));
    }

    [Fact]
    public void Constructor_RejectsNullCache()
    {
        var monitor = new TestOptionsMonitor<ResilientHttpClientOptions>(TestOptions.Fast());

        Assert.Throws<ArgumentNullException>(() =>
            new ResilientHttpMessageHandler("client", monitor, null!));
    }

    [Fact]
    public async Task AbsoluteUri_IsUsedAsIs()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("https://absolute.test/items?page=2");

        Assert.Equal("https://absolute.test/items?page=2", harness.Inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task RelativeUri_IsCombinedWithBaseAddress()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items/42");

        Assert.Equal("https://example.test/api/items/42", harness.Inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task RootRelativeUri_ReplacesBasePath()
    {
        var options = TestOptions.Fast(o => o.BaseAddress = "https://example.test/v1/");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("/root/items");

        Assert.Equal("https://example.test/root/items", harness.Inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task QueryString_IsPreserved()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items?a=1&b=two");

        Assert.Equal("?a=1&b=two", harness.Inner.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task RelativeUri_WithoutBaseAddress_Throws()
    {
        var options = TestOptions.Fast(o => o.BaseAddress = null);
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SendAsync("api/items"));

        Assert.Contains("BaseAddress", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Inner.CallCount);
    }

    [Fact]
    public async Task InvalidBaseAddress_Throws()
    {
        var options = TestOptions.Fast(o => o.BaseAddress = "not-an-uri");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SendAsync("api/items"));
    }

    [Fact]
    public async Task MissingRequestUri_Throws()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SendAsync());
    }

    [Fact]
    public async Task DefaultHeaders_AreApplied()
    {
        var options = TestOptions.Fast(o =>
        {
            o.DefaultHeaders["X-Default"] = "default-value";
        });

        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(["default-value"], harness.Inner.Requests[0].Headers["X-Default"]);
    }

    [Fact]
    public async Task RequestHeaders_OverrideDefaultHeaders()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["X-Test"] = "default");

        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        var request = new HttpRequestMessage(HttpMethod.Get, "api/items");
        request.Headers.TryAddWithoutValidation("X-Test", "request");

        using var response = await harness.SendAsync(request);

        Assert.Equal(["request"], harness.Inner.Requests[0].Headers["X-Test"]);
    }

    [Fact]
    public async Task DefaultHeaders_AreReloaded()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["X-Env"] = "one");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var first = await harness.SendAsync("api/items");
        var reloaded = TestOptions.Fast(o => o.DefaultHeaders["X-Env"] = "two");
        harness.Monitor.Update(reloaded);
        using var second = await harness.SendAsync("api/items");

        Assert.Equal(["one"], harness.Inner.Requests[0].Headers["X-Env"]);
        Assert.Equal(["two"], harness.Inner.Requests[1].Headers["X-Env"]);
    }

    [Fact]
    public async Task BaseAddress_IsReloaded()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var first = await harness.SendAsync("api/items");

        var reloaded = TestOptions.Fast(o => o.BaseAddress = "https://second.test/v2/");
        harness.Monitor.Update(reloaded);

        using var second = await harness.SendAsync("api/items");

        Assert.Equal("https://example.test/api/items", harness.Inner.Requests[0].RequestUri!.ToString());
        Assert.Equal("https://second.test/v2/api/items", harness.Inner.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task TotalTimeout_ThrowsTaskCanceledException()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Timeout = TimeSpan.FromMilliseconds(100);
            o.Resilience.Retry.Enabled = false;
        });

        using var harness = new ResilientHandlerHarness(options, SlowHandler(TimeSpan.FromSeconds(5)));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.SendAsync("api/items"));

        Assert.IsType<TaskCanceledException>(exception);
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Contains("таймаут", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagatedAsIs()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Timeout = TimeSpan.FromSeconds(30);
            o.Resilience.Retry.Enabled = false;
        });

        using var harness = new ResilientHandlerHarness(options, SlowHandler(TimeSpan.FromSeconds(5)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.SendAsync("api/items", cancellationToken: cancellation.Token));

        Assert.False(exception.InnerException is TimeoutException);
    }

    [Fact]
    public async Task Retry_RecoversAfterTransientStatuses()
    {
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Sequence(HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway, HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_Exhausted_ReturnsLastResponse()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.MaxRetryAttempts = 2);
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(3, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_DoesNotRetryClientError()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.BadRequest));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_UsesConfiguredStatusCodes()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.RetryStatusCodes = [(int)(HttpStatusCode)418]);

        using var harness = new ResilientHandlerHarness(
            options,
            RecordingHttpMessageHandler.Sequence((HttpStatusCode)418, HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_RecoversAfterExceptions()
    {
        var inner = new RecordingHttpMessageHandler((index, _, _) =>
            index < 2
                ? throw new HttpRequestException("transport failure")
                : Task.FromResult(Responses.Ok()));

        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), inner);

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_ResendsBodyAndContentHeaders_WhenNonIdempotentRetryEnabled()
    {
        var inner = new RecordingHttpMessageHandler((index, _, _) =>
            Task.FromResult(index == 0 ? Responses.Status(HttpStatusCode.ServiceUnavailable) : Responses.Ok()));

        var options = TestOptions.Fast(o => o.Resilience.Retry.RetryNonIdempotentMethods = true);
        using var harness = new ResilientHandlerHarness(options, inner);
        using var content = new StringContent("{\"name\":\"value\"}", Encoding.UTF8, "application/json");

        using var response = await harness.SendAsync("api/items", HttpMethod.Post, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
        Assert.Equal("{\"name\":\"value\"}", inner.Requests[0].BodyText);
        Assert.Equal("{\"name\":\"value\"}", inner.Requests[1].BodyText);
        Assert.Contains("application/json", inner.Requests[1].ContentHeaders["Content-Type"].Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_DoesNotRetryPostByDefault()
    {
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Status(HttpStatusCode.InternalServerError));
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await harness.SendAsync("api/items", HttpMethod.Post, content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_DoesNotRetryPatchByDefault()
    {
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        using var response = await harness.SendAsync("api/items", HttpMethod.Patch);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Retry_RetriesIdempotentMethodsByDefault(string method)
    {
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Sequence(HttpStatusCode.InternalServerError, HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items", new HttpMethod(method));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_RetriesPost_WhenNonIdempotentRetryEnabled()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.RetryNonIdempotentMethods = true);
        using var harness = new ResilientHandlerHarness(
            options,
            RecordingHttpMessageHandler.Sequence(HttpStatusCode.InternalServerError, HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items", HttpMethod.Post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Retry_DisposesIntermediateResponses()
    {
        var firstContent = new TrackingContent();
        var secondContent = new TrackingContent();

        var inner = new RecordingHttpMessageHandler((index, _, _) => Task.FromResult(index switch
        {
            0 => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = firstContent },
            1 => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = secondContent },
            _ => Responses.Ok(),
        }));

        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), inner);

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(firstContent.IsDisposed);
        Assert.True(secondContent.IsDisposed);
    }

    [Fact]
    public async Task ResilienceDisabled_DoesNotRetry()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.WithoutResilience(), RecordingHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    [Fact]
    public async Task RetryPolicy_IsReloaded()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.MaxRetryAttempts = 0);
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        using var first = await harness.SendAsync("api/items");
        Assert.Equal(1, harness.Inner.CallCount);

        var reloaded = TestOptions.Fast(o => o.Resilience.Retry.MaxRetryAttempts = 2);
        harness.Monitor.Update(reloaded);

        using var second = await harness.SendAsync("api/items");

        Assert.Equal(4, harness.Inner.CallCount);
    }

    [Fact]
    public async Task AttemptTimeout_IsRetried()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.MaxRetryAttempts = 1;
            o.Resilience.AttemptTimeout = new TimeoutOptions
            {
                Enabled = true,
                Timeout = TimeSpan.FromMilliseconds(50),
            };
        });

        var inner = new RecordingHttpMessageHandler(async (index, _, cancellationToken) =>
        {
            if (index == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return Responses.Ok();
        });

        using var harness = new ResilientHandlerHarness(options, inner);

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task CircuitBreaker_StopsCallsToInnerHandler()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.CircuitBreaker = new CircuitBreakerOptions
            {
                Enabled = true,
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(10),
            };
        });

        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        using var first = await harness.SendAsync("api/items");
        using var second = await harness.SendAsync("api/items");

        await Assert.ThrowsAsync<BrokenCircuitException>(() => harness.SendAsync("api/items"));

        Assert.Equal(2, harness.Inner.CallCount);
    }

    [Fact]
    public async Task DefaultHeaders_AreAppliedToEveryAttempt()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["X-Attempt"] = "yes");
        var inner = RecordingHttpMessageHandler.Sequence(HttpStatusCode.InternalServerError, HttpStatusCode.OK);

        using var harness = new ResilientHandlerHarness(options, inner);

        using var response = await harness.SendAsync("api/items");

        Assert.All(inner.Requests, request => Assert.Equal(["yes"], request.Headers["X-Attempt"]));
    }

    [Fact]
    public async Task RequestOptions_AreCopied()
    {
        var inner = RecordingHttpMessageHandler.Status(HttpStatusCode.OK);
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), inner);

        var request = new HttpRequestMessage(HttpMethod.Get, "api/items");
        request.Options.Set(new HttpRequestOptionsKey<string>("custom-key"), "custom-value");

        using var response = await harness.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("custom-key", inner.Requests[0].OptionKeys);
    }

    [Fact]
    public async Task NullDefaultHeaders_DoesNotBreakRequest()
    {
        var options = TestOptions.Fast();
        options.DefaultHeaders = null!;

        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Headers_DoNotLeakBetweenRequests()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        var first = new HttpRequestMessage(HttpMethod.Get, "api/items");
        first.Headers.TryAddWithoutValidation("X-Only-First", "1");
        using var firstResponse = await harness.SendAsync(first);

        using var secondResponse = await harness.SendAsync("api/items");

        Assert.True(harness.Inner.Requests[0].Headers.ContainsKey("X-Only-First"));
        Assert.False(harness.Inner.Requests[1].Headers.ContainsKey("X-Only-First"));
    }

    [Fact]
    public async Task BaseAddress_WithoutTrailingSlash_IsTreatedAsDirectory()
    {
        var options = TestOptions.Fast(o => o.BaseAddress = "https://example.test/v1");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal("https://example.test/v1/api/items", harness.Inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task DynamicBaseAddress_IsResolvedToConfiguredBase()
    {
        var options = TestOptions.Fast(o => o.BaseAddress = "https://configured.test/v1/");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(ResilientHttpMessageHandler.DynamicBaseAddress, "api/items"));

        using var response = await harness.SendAsync(request);

        Assert.Equal("https://configured.test/v1/api/items", harness.Inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task DynamicBaseAddress_RootRelativePath_ReplacesBasePath()
    {
        var options = TestOptions.Fast(o => o.BaseAddress = "https://configured.test/v1/");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(ResilientHttpMessageHandler.DynamicBaseAddress, "/root/items"));

        using var response = await harness.SendAsync(request);

        Assert.Equal("https://configured.test/root/items", harness.Inner.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task DisabledResilience_StillAppliesHeadersAndBaseAddress()
    {
        var options = TestOptions.WithoutResilience();
        options.DefaultHeaders["X-Still"] = "yes";
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal("https://example.test/api/items", harness.Inner.Requests[0].RequestUri!.ToString());
        Assert.Equal(["yes"], harness.Inner.Requests[0].Headers["X-Still"]);
    }

    [Fact]
    public async Task NoRetry_ReusesOriginalContentWithoutBuffering()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        using var content = new StringContent("{\"name\":\"value\"}", Encoding.UTF8, "application/json");

        using var response = await harness.SendAsync("api/items", HttpMethod.Post, content);

        Assert.Same(content, harness.Inner.Requests[0].Content);
    }

    [Fact]
    public async Task Retry_BuffersContentSoItCanBeReplayed()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.RetryNonIdempotentMethods = true);
        using var harness = new ResilientHandlerHarness(
            options,
            RecordingHttpMessageHandler.Sequence(HttpStatusCode.InternalServerError, HttpStatusCode.OK));
        using var content = new StringContent("{\"name\":\"value\"}", Encoding.UTF8, "application/json");

        using var response = await harness.SendAsync("api/items", HttpMethod.Post, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, harness.Inner.CallCount);
        Assert.IsType<ByteArrayContent>(harness.Inner.Requests[0].Content);
        Assert.NotSame(harness.Inner.Requests[0].Content, harness.Inner.Requests[1].Content);
        Assert.Equal("{\"name\":\"value\"}", harness.Inner.Requests[0].BodyText);
        Assert.Equal("{\"name\":\"value\"}", harness.Inner.Requests[1].BodyText);
    }

    [Fact]
    public async Task NoRetry_StreamContentIsPassedAsIs()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("stream-data"));
        using var content = new StreamContent(stream);

        using var response = await harness.SendAsync("api/upload", HttpMethod.Post, content);

        Assert.Same(content, harness.Inner.Requests[0].Content);
        Assert.Equal("stream-data", harness.Inner.Requests[0].BodyText);
    }

    [Fact]
    public async Task DefaultHeaders_ContentHeaderIsAppliedToContent()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["Content-Type"] = "application/json");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        using var content = new ByteArrayContent([1, 2, 3]);

        using var response = await harness.SendAsync("api/items", HttpMethod.Post, content);

        Assert.Equal(["application/json"], harness.Inner.Requests[0].ContentHeaders["Content-Type"]);
        Assert.False(harness.Inner.Requests[0].Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task DefaultHeaders_DoNotOverrideContentTypeOfRequest()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["Content-Type"] = "text/plain");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await harness.SendAsync("api/items", HttpMethod.Post, content);

        Assert.Equal(["application/json; charset=utf-8"], harness.Inner.Requests[0].ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task DefaultHeaders_ContentHeaderWithoutContent_IsIgnored()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["Content-Type"] = "application/json");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(harness.Inner.Requests[0].Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task DefaultHeaders_AreAppliedToAbsoluteUri()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["X-Default"] = "yes");
        using var harness = new ResilientHandlerHarness(options, RecordingHttpMessageHandler.Status(HttpStatusCode.OK));

        using var response = await harness.SendAsync("https://absolute.test/items");

        Assert.Equal(["yes"], harness.Inner.Requests[0].Headers["X-Default"]);
    }

    [Fact]
    public async Task Timeout_Disabled_DoesNotCancelSlowRequest()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Timeout = TimeSpan.Zero;
            o.Resilience.Retry.Enabled = false;
        });

        using var harness = new ResilientHandlerHarness(options, SlowHandler(TimeSpan.FromMilliseconds(50)));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CustomMethod_IsNotRetriedByDefault()
    {
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Sequence(HttpStatusCode.InternalServerError, HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items", new HttpMethod("CUSTOM"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    [Fact]
    public async Task RetryNonIdempotentMethods_WithZeroAttempts_DoesNotRetry()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.RetryNonIdempotentMethods = true;
            o.Resilience.Retry.MaxRetryAttempts = 0;
        });

        using var harness = new ResilientHandlerHarness(
            options,
            RecordingHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        using var response = await harness.SendAsync("api/items", HttpMethod.Post);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    [Fact]
    public async Task AlreadyCancelledToken_DoesNotCallInnerHandler()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.SendAsync("api/items", cancellationToken: cancellation.Token));

        Assert.Equal(0, harness.Inner.CallCount);
    }

    [Fact]
    public async Task ResponseStatusAndHeaders_AreReturned()
    {
        var inner = new RecordingHttpMessageHandler((_, _, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.TryAddWithoutValidation("X-Request-Id", "abc");
            return Task.FromResult(response);
        });

        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), inner);

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["abc"], response.Headers.GetValues("X-Request-Id"));
    }

    [Fact]
    public async Task RequestVersionAndPolicy_AreCopied()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        var request = new HttpRequestMessage(HttpMethod.Get, "api/items")
        {
            Version = new Version(2, 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        using var response = await harness.SendAsync(request);

        Assert.Equal(new Version(2, 0), harness.Inner.Requests[0].Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, harness.Inner.Requests[0].VersionPolicy);
    }

    [Fact]
    public async Task MultipleHeaderValues_AreCopied()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.Fast(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        var request = new HttpRequestMessage(HttpMethod.Get, "api/items");
        request.Headers.TryAddWithoutValidation("X-Multi", ["one", "two"]);

        using var response = await harness.SendAsync(request);

        Assert.Equal(["one", "two"], harness.Inner.Requests[0].Headers["X-Multi"]);
    }

    [Fact]
    public async Task ResilienceDisabled_WithContent_DoesNotBuffer()
    {
        using var harness = new ResilientHandlerHarness(TestOptions.WithoutResilience(), RecordingHttpMessageHandler.Status(HttpStatusCode.OK));
        using var content = new StringContent("payload", Encoding.UTF8);

        using var response = await harness.SendAsync("api/items", HttpMethod.Put, content);

        Assert.Same(content, harness.Inner.Requests[0].Content);
        Assert.Equal("payload", harness.Inner.Requests[0].BodyText);
    }

    [Fact]
    public async Task Retry_LogsRequestAndRetryMessages()
    {
        var entries = new List<LogEntry>();
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Sequence(HttpStatusCode.InternalServerError, HttpStatusCode.OK),
            loggerFactory: new RecordingLoggerFactory(entries));

        using var response = await harness.SendAsync("api/items");

        Assert.Contains(entries, entry =>
            entry.Message.Contains("test-client", StringComparison.Ordinal) &&
            entry.Message.Contains("повторы", StringComparison.Ordinal));
        Assert.Contains(entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("повтор", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CircuitBreaker_LogsOpenAndClose()
    {
        var entries = new List<LogEntry>();
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.CircuitBreaker = new CircuitBreakerOptions
            {
                Enabled = true,
                FailureRatio = 0.5,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromMilliseconds(500),
                BreakDuration = TimeSpan.FromMilliseconds(500),
            };
        });

        using var harness = new ResilientHandlerHarness(
            options,
            new RecordingHttpMessageHandler((index, _, _) =>
                Task.FromResult(index < 2 ? Responses.Status(HttpStatusCode.InternalServerError) : Responses.Ok())),
            loggerFactory: new RecordingLoggerFactory(entries));

        using var first = await harness.SendAsync("api/items");
        using var second = await harness.SendAsync("api/items");

        Assert.Contains(entries, entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("предохранитель разомкнут", StringComparison.OrdinalIgnoreCase));

        await Task.Delay(TimeSpan.FromMilliseconds(700));

        using var third = await harness.SendAsync("api/items");

        Assert.Contains(entries, entry =>
            entry.Message.Contains("предохранитель закрыт", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Retry_RetriesAllDefaultTransientStatusCodes(int statusCode)
    {
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Sequence((HttpStatusCode)statusCode, HttpStatusCode.OK));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, harness.Inner.CallCount);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(501)]
    public async Task Retry_DoesNotRetryNonTransientStatusCodes(int statusCode)
    {
        using var harness = new ResilientHandlerHarness(
            TestOptions.Fast(),
            RecordingHttpMessageHandler.Status((HttpStatusCode)statusCode));

        using var response = await harness.SendAsync("api/items");

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    private static RecordingHttpMessageHandler SlowHandler(TimeSpan delay) =>
        new(async (_, _, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            return Responses.Ok();
        });
}
