using System;

namespace Common.Helper.Http;

/// <summary>
/// Границы значений, которые поддерживают Polly и таймеры .NET.
/// Проверяются при валидации параметров, чтобы вместо исключения при первом запросе
/// приложение получило понятную ошибку конфигурации.
/// </summary>
internal static class ResilienceLimits
{
    /// <summary>Минимальная длительность окна выборки и размыкания предохранителя (требование Polly).</summary>
    public static readonly TimeSpan MinCircuitBreakerDuration = TimeSpan.FromMilliseconds(500);

    /// <summary>Максимальная длительность окна выборки и размыкания предохранителя (требование Polly).</summary>
    public static readonly TimeSpan MaxCircuitBreakerDuration = TimeSpan.FromDays(1);

    /// <summary>Минимальное количество вызовов, после которого предохранитель может разомкнуться (требование Polly).</summary>
    public const int MinCircuitBreakerThroughput = 2;

    /// <summary>Минимальная длительность таймаута одной попытки (требование Polly).</summary>
    public static readonly TimeSpan MinAttemptTimeout = TimeSpan.FromMilliseconds(10);

    /// <summary>Максимальная длительность таймаута одной попытки (требование Polly).</summary>
    public static readonly TimeSpan MaxAttemptTimeout = TimeSpan.FromDays(1);

    /// <summary>Максимальная задержка перед повтором (требование Polly).</summary>
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// Максимальный общий таймаут запроса: ограничение <see cref="System.Threading.CancellationTokenSource"/>
    /// (около 49,7 суток).
    /// </summary>
    public static readonly TimeSpan MaxTotalTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
}
