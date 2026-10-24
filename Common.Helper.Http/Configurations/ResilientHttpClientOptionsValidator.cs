using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Common.Helper.Http;

/// <summary>
/// Проверяет параметры HTTP-клиента после привязки к конфигурации.
/// Ошибки выводятся как <see cref="OptionsValidationException"/> при первом обращении к параметрам.
/// </summary>
public sealed class ResilientHttpClientOptionsValidator : IValidateOptions<ResilientHttpClientOptions>
{
    private static readonly string[] ForbiddenHeaderValueCharacters = ["\r", "\n"];

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ResilientHttpClientOptions options)
    {
        var failures = new List<string>();
        var prefix = string.IsNullOrEmpty(name) ? "ResilientHttpClient" : $"ResilientHttpClient '{name}'";

        if (options is null)
        {
            return ValidateOptionsResult.Fail($"{prefix}: параметры не заданы.");
        }

        ValidateBaseAddress(prefix, options, failures);
        ValidateTimeout(prefix, options, failures);
        ValidateHeaders(prefix, options, failures);

        if (options.Resilience is null)
        {
            failures.Add($"{prefix}: секция Resilience не задана.");
        }
        else
        {
            ValidateRetry(prefix, options.Resilience, failures);
            ValidateCircuitBreaker(prefix, options.Resilience, failures);
            ValidateAttemptTimeout(prefix, options.Resilience, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateBaseAddress(string prefix, ResilientHttpClientOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            return;
        }

        if (!Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var uri))
        {
            failures.Add($"{prefix}: BaseAddress '{options.BaseAddress}' не является абсолютным URI.");
            return;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            failures.Add($"{prefix}: BaseAddress '{options.BaseAddress}' должен использовать схему http или https.");
        }
    }

    private static void ValidateTimeout(string prefix, ResilientHttpClientOptions options, List<string> failures)
    {
        if (options.Timeout < TimeSpan.Zero)
        {
            failures.Add($"{prefix}: Timeout не может быть отрицательным.");
        }
    }

    private static void ValidateHeaders(string prefix, ResilientHttpClientOptions options, List<string> failures)
    {
        if (options.DefaultHeaders is null)
        {
            failures.Add($"{prefix}: DefaultHeaders не может быть null.");
            return;
        }

        foreach (var header in options.DefaultHeaders)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                failures.Add($"{prefix}: имя заголовка по умолчанию не может быть пустым.");
                continue;
            }

            if (header.Value is not null && ForbiddenHeaderValueCharacters.Any(header.Value.Contains))
            {
                failures.Add($"{prefix}: значение заголовка '{header.Key}' содержит перевод строки.");
            }
        }
    }

    private static void ValidateRetry(string prefix, ResilienceOptions resilience, List<string> failures)
    {
        var retry = resilience.Retry;
        if (retry is null)
        {
            failures.Add($"{prefix}: секция Resilience:Retry не задана.");
            return;
        }

        if (!retry.Enabled)
        {
            return;
        }

        if (retry.MaxRetryAttempts < 0)
        {
            failures.Add($"{prefix}: Resilience:Retry:MaxRetryAttempts не может быть отрицательным.");
        }

        if (retry.Delay < TimeSpan.Zero)
        {
            failures.Add($"{prefix}: Resilience:Retry:Delay не может быть отрицательным.");
        }

        if (retry.MaxDelay < TimeSpan.Zero)
        {
            failures.Add($"{prefix}: Resilience:Retry:MaxDelay не может быть отрицательным.");
        }

        if (retry.MaxDelay > TimeSpan.Zero && retry.MaxDelay < retry.Delay)
        {
            failures.Add($"{prefix}: Resilience:Retry:MaxDelay не может быть меньше Delay.");
        }

        ValidateStatusCodes(prefix, "Resilience:Retry:RetryStatusCodes", retry.RetryStatusCodes, failures);
    }

    private static void ValidateCircuitBreaker(string prefix, ResilienceOptions resilience, List<string> failures)
    {
        var circuitBreaker = resilience.CircuitBreaker;
        if (circuitBreaker is null)
        {
            failures.Add($"{prefix}: секция Resilience:CircuitBreaker не задана.");
            return;
        }

        if (!circuitBreaker.Enabled)
        {
            return;
        }

        if (circuitBreaker.FailureRatio <= 0 || circuitBreaker.FailureRatio > 1)
        {
            failures.Add($"{prefix}: Resilience:CircuitBreaker:FailureRatio должен быть в диапазоне (0, 1].");
        }

        if (circuitBreaker.MinimumThroughput <= 0)
        {
            failures.Add($"{prefix}: Resilience:CircuitBreaker:MinimumThroughput должен быть больше нуля.");
        }

        if (circuitBreaker.SamplingDuration <= TimeSpan.Zero)
        {
            failures.Add($"{prefix}: Resilience:CircuitBreaker:SamplingDuration должен быть больше нуля.");
        }

        if (circuitBreaker.BreakDuration <= TimeSpan.Zero)
        {
            failures.Add($"{prefix}: Resilience:CircuitBreaker:BreakDuration должен быть больше нуля.");
        }

        ValidateStatusCodes(prefix, "Resilience:CircuitBreaker:BreakStatusCodes", circuitBreaker.BreakStatusCodes, failures);
    }

    private static void ValidateAttemptTimeout(string prefix, ResilienceOptions resilience, List<string> failures)
    {
        var attemptTimeout = resilience.AttemptTimeout;
        if (attemptTimeout is null)
        {
            failures.Add($"{prefix}: секция Resilience:AttemptTimeout не задана.");
            return;
        }

        if (attemptTimeout.Enabled && attemptTimeout.Timeout <= TimeSpan.Zero)
        {
            failures.Add($"{prefix}: Resilience:AttemptTimeout:Timeout должен быть больше нуля при включённом таймауте.");
        }

        if (!attemptTimeout.Enabled && attemptTimeout.Timeout < TimeSpan.Zero)
        {
            failures.Add($"{prefix}: Resilience:AttemptTimeout:Timeout не может быть отрицательным.");
        }
    }

    private static void ValidateStatusCodes(string prefix, string path, int[]? statusCodes, List<string> failures)
    {
        if (statusCodes is null)
        {
            return;
        }

        foreach (var statusCode in statusCodes)
        {
            if (statusCode is < 100 or > 599)
            {
                failures.Add($"{prefix}: {path} содержит недопустимый код ответа {statusCode}.");
            }
        }
    }
}
