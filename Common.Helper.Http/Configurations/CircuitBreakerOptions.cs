using System;

namespace Common.Helper.Http;

/// <summary>Параметры предохранителя (circuit breaker).</summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>Включает предохранитель.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Доля неуспешных вызовов, при которой предохранитель размыкается. От 0 до 1.</summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>Минимальное число вызовов в окне выборки, после которого предохранитель может разомкнуться.</summary>
    public int MinimumThroughput { get; set; } = 100;

    /// <summary>Длительность окна выборки.</summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Длительность разомкнутого состояния, после которого выполняется пробный вызов.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Коды ответа, которые считаются неуспешными для предохранителя. Если не задано,
    /// используются стандартные: 408, 429, 500, 502, 503, 504.
    /// </summary>
    public int[]? BreakStatusCodes { get; set; }
}
