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

    [Fact]
    public void ResilienceDisabled_ReturnsSharedEmptyPipeline()
    {
        var pipeline = ResiliencePipelineFactory.Create(TestOptions.WithoutResilience(), "test");

        Assert.Same(ResiliencePipeline<HttpResponseMessage>.Empty, pipeline);
    }

    [Fact]
    public void RetryDisabled_ExecutesOnce()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.Enabled = false);
        var pipeline = ResiliencePipelineFactory.Create(options, "test");
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
    public void RetryWithZeroAttempts_ExecutesOnce()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.MaxRetryAttempts = 0);
        var pipeline = ResiliencePipelineFactory.Create(options, "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void AllowRetryFalse_ExecutesOnce()
    {
        var pipeline = ResiliencePipelineFactory.Create(TestOptions.Fast(), "test", allowRetry: false);
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void AllStrategiesDisabled_ExecutesOnce()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.CircuitBreaker.Enabled = false;
            o.Resilience.AttemptTimeout.Enabled = false;
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void CreateRetryOptions_MapsAllSettings()
    {
        var retry = new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 4,
            Delay = TimeSpan.FromMilliseconds(250),
            MaxDelay = TimeSpan.FromSeconds(3),
            BackoffType = DelayBackoffType.Linear,
            UseJitter = false,
            RetryStatusCodes = [418, 429],
        };

        var mapped = ResiliencePipelineFactory.CreateRetryOptions(retry, "client");

        Assert.Equal(4, mapped.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(250), mapped.Delay);
        Assert.Equal(TimeSpan.FromSeconds(3), mapped.MaxDelay);
        Assert.Equal(DelayBackoffType.Linear, mapped.BackoffType);
        Assert.False(mapped.UseJitter);
        Assert.NotNull(mapped.ShouldHandle);
        Assert.NotNull(mapped.OnRetry);
    }

    [Fact]
    public void CreateRetryOptions_ZeroMaxDelay_MeansNoCap()
    {
        var retry = new RetryOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.Zero,
        };

        var mapped = ResiliencePipelineFactory.CreateRetryOptions(retry, "client");

        Assert.Null(mapped.MaxDelay);
    }

    [Fact]
    public void CreateCircuitBreakerOptions_MapsAllSettings()
    {
        var circuitBreaker = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureRatio = 0.25,
            MinimumThroughput = 10,
            SamplingDuration = TimeSpan.FromSeconds(20),
            BreakDuration = TimeSpan.FromSeconds(7),
            BreakStatusCodes = [500, 503],
        };

        var mapped = ResiliencePipelineFactory.CreateCircuitBreakerOptions(circuitBreaker, "client");

        Assert.Equal(0.25, mapped.FailureRatio);
        Assert.Equal(10, mapped.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(20), mapped.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(7), mapped.BreakDuration);
        Assert.NotNull(mapped.ShouldHandle);
        Assert.NotNull(mapped.OnOpened);
        Assert.NotNull(mapped.OnClosed);
    }

    [Fact]
    public void NullStrategyOptions_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ResiliencePipelineFactory.CreateRetryOptions(null!, "client"));
        Assert.Throws<ArgumentNullException>(() =>
            ResiliencePipelineFactory.CreateCircuitBreakerOptions(null!, "client"));
        Assert.Throws<ArgumentNullException>(() =>
            ResiliencePipelineFactory.Create(null!, "client"));
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
                Timeout = TimeSpan.FromMilliseconds(30),
            };
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "test");
        var calls = 0;

        using var response = await pipeline.ExecuteAsync(async ct =>
        {
            calls++;
            if (calls == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(2, calls);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void AttemptTimeoutDisabled_DoesNotLimitAttempt()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.AttemptTimeout = new TimeoutOptions
            {
                Enabled = false,
                Timeout = TimeSpan.FromMilliseconds(10),
            };
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "test");

        using var response = pipeline.Execute(() =>
        {
            Thread.Sleep(50);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void CircuitBreaker_DoesNotHandleClientError()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.CircuitBreaker = FastCircuitBreaker();
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "test");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = pipeline.Execute(() => new HttpResponseMessage(HttpStatusCode.BadRequest));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var success = pipeline.Execute(() => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    [Fact]
    public void CircuitBreaker_UsesCustomBreakStatusCodes()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.CircuitBreaker = FastCircuitBreaker();
            o.Resilience.CircuitBreaker.BreakStatusCodes = [418];
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "test");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = pipeline.Execute(() => new HttpResponseMessage((HttpStatusCode)418));
            Assert.Equal((HttpStatusCode)418, response.StatusCode);
        }

        Assert.Throws<BrokenCircuitException>(() => pipeline.Execute(() => new HttpResponseMessage(HttpStatusCode.OK)));
    }

    [Fact]
    public async Task CircuitBreaker_ClosesAfterBreakDuration()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.CircuitBreaker = FastCircuitBreaker();
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "test");
        var calls = 0;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = pipeline.Execute(() =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
        }

        Assert.Throws<BrokenCircuitException>(() => pipeline.Execute(() => new HttpResponseMessage(HttpStatusCode.OK)));

        await Task.Delay(TimeSpan.FromMilliseconds(700));

        using var recovered = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Retry_EmptyStatusCodes_UsesDefaults()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.RetryStatusCodes = []);
        var pipeline = ResiliencePipelineFactory.Create(options, "test");
        var calls = 0;

        using var response = pipeline.Execute(() =>
        {
            calls++;
            return new HttpResponseMessage(calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });

        Assert.Equal(2, calls);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void NullResilience_ReturnsSharedEmptyPipeline()
    {
        var options = TestOptions.Fast();
        options.Resilience = null!;

        var pipeline = ResiliencePipelineFactory.Create(options, "test");

        Assert.Same(ResiliencePipeline<HttpResponseMessage>.Empty, pipeline);
    }

    private static CircuitBreakerOptions FastCircuitBreaker() => new()
    {
        Enabled = true,
        FailureRatio = 0.5,
        MinimumThroughput = 2,
        SamplingDuration = TimeSpan.FromMilliseconds(500),
        BreakDuration = TimeSpan.FromMilliseconds(500),
    };
}
