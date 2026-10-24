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
/// Стратегии добавляются в порядке «снаружи внутрь»: повтор → предохранитель → таймаут попытки.
/// </summary>
internal static class ResiliencePipelineFactory
{
    private static readonly HashSet<int> DefaultTransientStatusCodes = [408, 429, 500, 502, 503, 504];

    public static ResiliencePipeline<HttpResponseMessage> Create(
        ResilientHttpClientOptions options,
        string clientName,
        bool allowRetry = true,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resilience = options.Resilience;
        if (resilience is null || !resilience.Enabled)
        {
            return ResiliencePipeline<HttpResponseMessage>.Empty;
        }

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        if (allowRetry && resilience.Retry is { Enabled: true, MaxRetryAttempts: > 0 } retry)
        {
            builder.AddRetry(CreateRetryOptions(retry, clientName, logger));
        }

        if (resilience.CircuitBreaker is { Enabled: true } circuitBreaker)
        {
            builder.AddCircuitBreaker(CreateCircuitBreakerOptions(circuitBreaker, clientName, logger));
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

    internal static HttpRetryStrategyOptions CreateRetryOptions(
        RetryOptions retry,
        string clientName,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(retry);

        var statusCodes = GetStatusCodes(retry.RetryStatusCodes);

        return new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = retry.MaxRetryAttempts,
            Delay = retry.Delay,

            // Ноль означает «без верхней границы»: Polly понимает это как null.
            MaxDelay = retry.MaxDelay > TimeSpan.Zero ? retry.MaxDelay : null,
            BackoffType = retry.BackoffType,
            UseJitter = retry.UseJitter,
            ShouldHandle = args => ValueTask.FromResult(ShouldHandleOutcome(args.Outcome, statusCodes)),
            OnRetry = args =>
            {
                // Неуспешный ответ выбрасывается: следующей попытке нужен новый экземпляр.
                args.Outcome.Result?.Dispose();

                logger?.LogWarning(
                    "HTTP client '{ClientName}': повтор {AttemptNumber} через {RetryDelay} после {Outcome}.",
                    clientName,
                    args.AttemptNumber,
                    args.RetryDelay,
                    args.Outcome.Exception?.Message ?? (object?)args.Outcome.Result?.StatusCode);

                return default;
            },
        };
    }

    internal static HttpCircuitBreakerStrategyOptions CreateCircuitBreakerOptions(
        CircuitBreakerOptions circuitBreaker,
        string clientName,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(circuitBreaker);

        var statusCodes = GetStatusCodes(circuitBreaker.BreakStatusCodes);

        return new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = circuitBreaker.FailureRatio,
            MinimumThroughput = circuitBreaker.MinimumThroughput,
            SamplingDuration = circuitBreaker.SamplingDuration,
            BreakDuration = circuitBreaker.BreakDuration,
            ShouldHandle = args => ValueTask.FromResult(ShouldHandleOutcome(args.Outcome, statusCodes)),
            OnOpened = args =>
            {
                logger?.LogError(
                    "HTTP client '{ClientName}': предохранитель разомкнут на {BreakDuration}.",
                    clientName,
                    args.BreakDuration);
                return default;
            },
            OnClosed = _ =>
            {
                logger?.LogInformation("HTTP client '{ClientName}': предохранитель закрыт.", clientName);
                return default;
            },
        };
    }

    private static IReadOnlySet<int> GetStatusCodes(int[]? configuredStatusCodes) =>
        configuredStatusCodes is { Length: > 0 }
            ? configuredStatusCodes.ToHashSet()
            : DefaultTransientStatusCodes;

    private static bool ShouldHandleOutcome(Outcome<HttpResponseMessage> outcome, IReadOnlySet<int> statusCodes)
    {
        // Сетевые ошибки и таймаут попытки имеют смысл повторять: запрос до сервера не дошёл
        // либо ответ не был получен.
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        return outcome.Result is { } response && statusCodes.Contains((int)response.StatusCode);
    }
}
