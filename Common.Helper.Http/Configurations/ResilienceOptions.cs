namespace Common.Helper.Http;

/// <summary>
/// Параметры устойчивости HTTP-клиента (реализованы стратегиями Polly через
/// <c>Microsoft.Extensions.Http.Resilience</c>).
/// </summary>
public sealed class ResilienceOptions
{
    /// <summary>Включает обработку устойчивости целиком. Если выключено, запросы отправляются без повторов и предохранителя.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Параметры повторов.</summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>Параметры предохранителя (circuit breaker).</summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>Таймаут одной попытки. Если попытка не уложилась, она считается неуспешной и может быть повторена.</summary>
    public TimeoutOptions AttemptTimeout { get; set; } = new();
}
