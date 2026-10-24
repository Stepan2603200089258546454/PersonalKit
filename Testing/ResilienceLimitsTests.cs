using System;
using Common.Helper.Http.Tests.Infrastructure;

namespace Common.Helper.Http.Tests;

/// <summary>
/// Проверяет, что границы, которые проверяет <see cref="ResilientHttpClientOptionsValidator"/>,
/// действительно принимаются Polly. Если Polly изменит требования, эти тесты упадут первыми.
/// </summary>
public sealed class ResilienceLimitsTests
{
    [Fact]
    public void CircuitBreaker_AtMinimalDurations_IsAccepted()
    {
        var options = TestOptions.Fast(o => o.Resilience.CircuitBreaker = new CircuitBreakerOptions
        {
            Enabled = true,
            MinimumThroughput = ResilienceLimits.MinCircuitBreakerThroughput,
            SamplingDuration = ResilienceLimits.MinCircuitBreakerDuration,
            BreakDuration = ResilienceLimits.MinCircuitBreakerDuration,
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "limits");

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CircuitBreaker_AtMaximalDurations_IsAccepted()
    {
        var options = TestOptions.Fast(o => o.Resilience.CircuitBreaker = new CircuitBreakerOptions
        {
            Enabled = true,
            MinimumThroughput = ResilienceLimits.MinCircuitBreakerThroughput,
            SamplingDuration = ResilienceLimits.MaxCircuitBreakerDuration,
            BreakDuration = ResilienceLimits.MaxCircuitBreakerDuration,
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "limits");

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void AttemptTimeout_AtMinimalValue_IsAccepted()
    {
        var options = TestOptions.Fast(o => o.Resilience.AttemptTimeout = new TimeoutOptions
        {
            Enabled = true,
            Timeout = ResilienceLimits.MinAttemptTimeout,
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "limits");

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void AttemptTimeout_AtMaximalValue_IsAccepted()
    {
        var options = TestOptions.Fast(o => o.Resilience.AttemptTimeout = new TimeoutOptions
        {
            Enabled = true,
            Timeout = ResilienceLimits.MaxAttemptTimeout,
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "limits");

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void RetryDelay_AtMaximalValue_IsAccepted()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Delay = ResilienceLimits.MaxRetryDelay;
            o.Resilience.Retry.MaxDelay = ResilienceLimits.MaxRetryDelay;
        });

        var pipeline = ResiliencePipelineFactory.Create(options, "limits");

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void TotalTimeoutLimit_MatchesCancellationTokenSourceLimit()
    {
        using var cancellation = new CancellationTokenSource(ResilienceLimits.MaxTotalTimeout);

        Assert.False(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void TotalTimeoutLimit_OverLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CancellationTokenSource(ResilienceLimits.MaxTotalTimeout + TimeSpan.FromMilliseconds(1)));
    }
}
