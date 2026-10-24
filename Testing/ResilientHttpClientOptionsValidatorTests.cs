using System;
using System.Linq;
using Common.Helper.Http.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Common.Helper.Http.Tests;

public sealed class ResilientHttpClientOptionsValidatorTests
{
    private readonly ResilientHttpClientOptionsValidator _validator = new();

    [Fact]
    public void ValidOptions_Pass()
    {
        var result = _validator.Validate("test", TestOptions.Fast());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void NullOptions_Fail()
    {
        var result = _validator.Validate("test", null!);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("не заданы", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("api.example.com")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com")]
    public void InvalidBaseAddress_Fail(string baseAddress)
    {
        var options = TestOptions.Fast();
        options.BaseAddress = baseAddress;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("BaseAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeTimeout_Fails()
    {
        var options = TestOptions.Fast();
        options.Timeout = TimeSpan.FromSeconds(-1);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("Timeout", StringComparison.Ordinal));
    }

    [Fact]
    public void NullHeaders_Fail()
    {
        var options = TestOptions.Fast();
        options.DefaultHeaders = null!;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("DefaultHeaders", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyHeaderName_Fails()
    {
        var options = TestOptions.Fast();
        options.DefaultHeaders[" "] = "value";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void HeaderWithNewLine_Fails()
    {
        var options = TestOptions.Fast();
        options.DefaultHeaders["X-Test"] = "a\r\nb";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("X-Test", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeRetryAttempts_Fail()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.MaxRetryAttempts = -1);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("MaxRetryAttempts", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeRetryDelay_Fails()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.Delay = TimeSpan.FromSeconds(-1));

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void MaxDelayLessThanDelay_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Delay = TimeSpan.FromSeconds(5);
            o.Resilience.Retry.MaxDelay = TimeSpan.FromSeconds(1);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("MaxDelay", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void InvalidRetryStatusCode_Fails(int statusCode)
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.RetryStatusCodes = [statusCode]);

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains(statusCode.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void DisabledRetry_SkipsRetryValidation()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Enabled = false;
            o.Resilience.Retry.MaxRetryAttempts = -5;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidFailureRatio_Fails(double ratio)
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.FailureRatio = ratio;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("FailureRatio", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidCircuitBreakerRanges_Fail()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.MinimumThroughput = 0;
            o.Resilience.CircuitBreaker.SamplingDuration = TimeSpan.Zero;
            o.Resilience.CircuitBreaker.BreakDuration = TimeSpan.Zero;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("MinimumThroughput", StringComparison.Ordinal));
        Assert.Contains(result.Failures, message => message.Contains("SamplingDuration", StringComparison.Ordinal));
        Assert.Contains(result.Failures, message => message.Contains("BreakDuration", StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledAttemptTimeoutWithZero_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.AttemptTimeout.Enabled = true;
            o.Resilience.AttemptTimeout.Timeout = TimeSpan.Zero;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("AttemptTimeout", StringComparison.Ordinal));
    }

    [Fact]
    public void DisabledAttemptTimeoutWithZero_Passes()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.AttemptTimeout.Enabled = false;
            o.Resilience.AttemptTimeout.Timeout = TimeSpan.Zero;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void NullResilience_Fails()
    {
        var options = TestOptions.Fast();
        options.Resilience = null!;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, message => message.Contains("Resilience", StringComparison.Ordinal));
    }

    [Fact]
    public void NullSubSections_Fail()
    {
        var options = TestOptions.Fast();
        options.Resilience.Retry = null!;
        options.Resilience.CircuitBreaker = null!;
        options.Resilience.AttemptTimeout = null!;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    [Fact]
    public void ValidationResult_ContainsClientName()
    {
        var options = TestOptions.Fast(o => o.Timeout = TimeSpan.FromSeconds(-1));

        var result = _validator.Validate("MyClient", options);

        Assert.Contains(result.Failures!, message => message.Contains("MyClient", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeMaxDelay_Fails()
    {
        var options = TestOptions.Fast(o => o.Resilience.Retry.MaxDelay = TimeSpan.FromSeconds(-1));

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("MaxDelay", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidBreakStatusCode_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.BreakStatusCodes = [42];
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("BreakStatusCodes", StringComparison.Ordinal));
    }

    [Fact]
    public void WhitespaceBaseAddress_Passes()
    {
        var options = TestOptions.Fast(o => o.BaseAddress = "   ");

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void NullHeaderValue_Passes()
    {
        var options = TestOptions.Fast(o => o.DefaultHeaders["X-Empty"] = null!);

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void DisabledAttemptTimeoutWithNegativeTimeout_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.AttemptTimeout.Enabled = false;
            o.Resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(-1);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void NullName_IsSupported()
    {
        var options = TestOptions.Fast(o => o.Timeout = TimeSpan.FromSeconds(-1));

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("ResilientHttpClient", StringComparison.Ordinal));
    }

    [Fact]
    public void TimeoutAboveCancellationTokenLimit_Fails()
    {
        var options = TestOptions.Fast(o => o.Timeout = ResilienceLimits.MaxTotalTimeout + TimeSpan.FromDays(1));

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("Timeout", StringComparison.Ordinal));
    }

    [Fact]
    public void TimeoutAtCancellationTokenLimit_Passes()
    {
        var options = TestOptions.Fast(o => o.Timeout = ResilienceLimits.MaxTotalTimeout);

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void RetryDelayAboveOneDay_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Delay = TimeSpan.FromDays(2);
            o.Resilience.Retry.MaxDelay = TimeSpan.FromDays(2);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("Delay", StringComparison.Ordinal));
    }

    [Fact]
    public void MaxDelayAboveOneDay_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Delay = TimeSpan.FromSeconds(1);
            o.Resilience.Retry.MaxDelay = TimeSpan.FromDays(2);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("MaxDelay", StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroMaxDelay_MeansNoCap_AndPasses()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Retry.Delay = TimeSpan.FromSeconds(1);
            o.Resilience.Retry.MaxDelay = TimeSpan.Zero;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void FailureRatioNaN_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.FailureRatio = double.NaN;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("FailureRatio", StringComparison.Ordinal));
    }

    [Fact]
    public void MinimumThroughputBelowTwo_Fails()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.MinimumThroughput = 1;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("MinimumThroughput", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(499)]
    [InlineData(86400001)]
    public void SamplingDurationOutsidePollyRange_Fails(int milliseconds)
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(milliseconds);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("SamplingDuration", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(499)]
    [InlineData(86400001)]
    public void BreakDurationOutsidePollyRange_Fails(int milliseconds)
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.BreakDuration = TimeSpan.FromMilliseconds(milliseconds);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("BreakDuration", StringComparison.Ordinal));
    }

    [Fact]
    public void CircuitBreakerAtPollyBoundaries_Passes()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = true;
            o.Resilience.CircuitBreaker.FailureRatio = 1;
            o.Resilience.CircuitBreaker.MinimumThroughput = 2;
            o.Resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(500);
            o.Resilience.CircuitBreaker.BreakDuration = TimeSpan.FromDays(1);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(86400001)]
    public void AttemptTimeoutOutsidePollyRange_Fails(int milliseconds)
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.AttemptTimeout.Enabled = true;
            o.Resilience.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(milliseconds);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, message => message.Contains("AttemptTimeout", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(86400000)]
    public void AttemptTimeoutAtPollyBoundaries_Passes(int milliseconds)
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.AttemptTimeout.Enabled = true;
            o.Resilience.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(milliseconds);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void DisabledCircuitBreaker_SkipsValidation()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.CircuitBreaker.Enabled = false;
            o.Resilience.CircuitBreaker.FailureRatio = 2;
            o.Resilience.CircuitBreaker.MinimumThroughput = 0;
            o.Resilience.CircuitBreaker.SamplingDuration = TimeSpan.Zero;
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void DisabledResilience_SkipsNestedValidation()
    {
        var options = TestOptions.Fast(o =>
        {
            o.Resilience.Enabled = false;
            o.Resilience.Retry.MaxRetryAttempts = -1;
            o.Resilience.Retry.Delay = TimeSpan.FromSeconds(-1);
        });

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }
}
