using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Common.Helper.Http.Tests.Infrastructure;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Common.Helper.Http.Tests;

public sealed class ResiliencePipelineFactoryTests
{
    [Fact]
    public void ResilienceDisabled_ExecutesOnce()
    {
        var pipeline = ResiliencePipelineFactory.Create(TestOptions.WithoutResilience(), "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        Assert.Equal(1, calls);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public void Retry_RetriesTransientStatusCodes()
    {
        var pipeline = ResiliencePipelineFactory.Create(TestOptions.Fast(), "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(calls < 3 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        });

        Assert.Equal(3, calls);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Retry_DoesNotRetryNonTransientStatusCode()
    {
        var pipeline = ResiliencePipelineFactory.Create(TestOptions.Fast(), "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        Assert.Equal(1, calls);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Retry_UsesCustomStatusCodes()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.RetryStatusCodes = [418]);
        var pipeline = ResiliencePipelineFactory.Create(options, "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(calls == 1 ? (HttpStatusCode)418 : HttpStatusCode.OK);
        });

        Assert.Equal(2, calls);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Retry_RetriesExceptions()
    {
        var pipeline = ResiliencePipelineFactory.Create(TestOptions.Fast(), "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            if (calls < 2)
            {
                throw new HttpRequestException("boom");
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(2, calls);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AttemptTimeout_ThrowsTimeoutRejectedException()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.AttemptTimeout = new TimeoutOptions
            {
                Enabled = true,
                Timeout = TimeSpan.FromMilliseconds(50),
            };
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "test");

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            pipeline.ExecuteAsync(async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }).AsTask());
    }

    [Fact]
    public void CircuitBreaker_OpensAfterFailures()
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

        var pipeline = ResiliencePipelineFactory.Create(options, "test");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = pipeline.Execute(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        Assert.Throws<BrokenCircuitException>(() => pipeline.Execute(() => new HttpResponseMessage(HttpStatusCode.OK)));
    }

    [Fact]
    public void EmptyResilience_SendsSingleAttempt()
    {
        var options = TestOptions.WithoutResilience();
        var pipeline = ResiliencePipelineFactory.Create(options, "test");

        using var response = pipeline.Execute(() => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
