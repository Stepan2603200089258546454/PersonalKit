using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace Common.Helper.Http;

/// <summary>
/// Собирает конвейер устойчивости по параметрам клиента.
/// </summary>
internal static class ResiliencePipelineFactory
{
    private static readonly int[] DefaultTransientStatusCodes = [408, 429, 500, 502, 503, 504];

    public static ResiliencePipeline<HttpResponseMessage> Create(
        ResilientHttpClientOptions options,
        string clientName,
        bool allowRetry = true,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        var resilience = options.Resilience;

        if (resilience is null || !resilience.Enabled)
        {
            return builder.Build();
        }

        if (allowRetry && resilience.Retry is { Enabled: true, MaxRetryAttempts: > 0 } retry)
        {
            builder.AddRetry(CreateRetryOptions(retry, clientName, logger));
        }

        if (resilience.CircuitBreaker is { Enabled: true })
        {
            builder.AddCircuitBreaker(CreateCircuitBreakerOptions(resilience.CircuitBreaker, clientName, logger));
        }

        if (resilience.AttemptTimeout is { Enabled: true } attemptTimeout && attemptTimeout.Timeout > TimeSpan.Zero)
        {
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = attemptTimeout.Timeout,
                OnTimeout = args =>
                {
                    logger?.LogWarning(
                        "HTTP client '{ClientName}': попытка превысила таймаут {Timeout}.",
                        clientName,
                        attemptTimeout.Timeout);
                    return default;
                },
            });
        }

        return builder.Build();
    }

    private static HttpRetryStrategyOptions CreateRetryOptions(
        RetryOptions retry,
        string clientName,
        ILogger? logger)
    {
        var statusCodes = GetStatusCodes(retry.RetryStatusCodes);

        return new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = retry.MaxRetryAttempts,
            Delay = retry.Delay,
            MaxDelay = retry.MaxDelay,
            BackoffType = retry.BackoffType,
            UseJitter = retry.UseJitter,
            ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome, statusCodes)),
            OnRetry = args =>
            {
                // Неуспешный ответ выбрасывается: следующей попытке нужен новый экземпляр.
                args.Outcome.Result?.Dispose();

                logger?.LogWarning(
                    "HTTP client '{ClientName}': повтор {AttemptNumber} после {Outcome}.",
                    clientName,
                    args.AttemptNumber,
                    args.Outcome.Exception?.Message ?? (object?)args.Outcome.Result?.StatusCode);

                return default;
            },
        };
    }

    private static HttpCircuitBreakerStrategyOptions CreateCircuitBreakerOptions(
        CircuitBreakerOptions circuitBreaker,
        string clientName,
        ILogger? logger)
    {
        var statusCodes = GetStatusCodes(circuitBreaker.BreakStatusCodes);

        return new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = circuitBreaker.FailureRatio,
            MinimumThroughput = circuitBreaker.MinimumThroughput,
            SamplingDuration = circuitBreaker.SamplingDuration,
            BreakDuration = circuitBreaker.BreakDuration,
            ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome, statusCodes)),
            OnOpened = _ =>
            {
                logger?.LogError("HTTP client '{ClientName}': предохранитель разомкнут.", clientName);
                return default;
            },
            OnClosed = _ =>
            {
                logger?.LogInformation("HTTP client '{ClientName}': предохранитель закрыт.", clientName);
                return default;
            },
        };
    }

    private static HashSet<int> GetStatusCodes(int[]? configuredStatusCodes)
    {
        var source = configuredStatusCodes is { Length: > 0 }
            ? configuredStatusCodes
            : DefaultTransientStatusCodes;

        return source.ToHashSet();
    }

    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, HashSet<int> statusCodes)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        return outcome.Result is { } response && statusCodes.Contains((int)response.StatusCode);
    }
}
