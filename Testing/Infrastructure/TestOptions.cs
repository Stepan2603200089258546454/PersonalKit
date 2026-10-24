using System;
using Polly;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Фабрики параметров для тестов: минимальные задержки, чтобы тесты были быстрыми.</summary>
internal static class TestOptions
{
    public const string DefaultBaseAddress = "https://example.test/";

    public static ResilientHttpClientOptions Fast(Action<ResilientHttpClientOptions>? configure = null)
    {
        var options = new ResilientHttpClientOptions
        {
            BaseAddress = DefaultBaseAddress,
            Timeout = TimeSpan.FromSeconds(5),
            Resilience = new ResilienceOptions
            {
                Enabled = true,
                Retry = new RetryOptions
                {
                    Enabled = true,
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMilliseconds(1),
                    MaxDelay = TimeSpan.FromMilliseconds(5),
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = false,
                },
                CircuitBreaker = new CircuitBreakerOptions
                {
                    Enabled = false,
                },
                AttemptTimeout = new TimeoutOptions
                {
                    Enabled = false,
                },
            },
        };

        configure?.Invoke(options);
        return options;
    }

    public static ResilientHttpClientOptions WithoutResilience()
    {
        var options = Fast();
        options.Resilience.Enabled = false;
        return options;
    }
}
