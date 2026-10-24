using System;
using Polly;

namespace Common.Helper.Http;

/// <summary>Параметры повторов неуспешных запросов.</summary>
public sealed class RetryOptions
{
    /// <summary>Включает повторы.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Количество повторов после первой попытки. Значение 3 означает до 4 попыток всего, 0 отключает повторы.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Базовая задержка перед повтором. Не больше одних суток.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Тип нарастания задержки: <c>Constant</c>, <c>Linear</c>, <c>Exponential</c>.</summary>
    public DelayBackoffType BackoffType { get; set; } = DelayBackoffType.Exponential;

    /// <summary>Добавляет случайный разброс к задержке.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Верхняя граница задержки между повторами. Не больше одних суток.
    /// <see cref="TimeSpan.Zero"/> отключает ограничение.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Коды ответа, при которых выполняется повтор. Если не задано, используются
    /// стандартные для REST: 408, 429, 500, 502, 503, 504.
    /// </summary>
    public int[]? RetryStatusCodes { get; set; }

    /// <summary>
    /// Повторять ли неидемпотентные методы (POST, PATCH). По умолчанию <c>false</c>:
    /// повтор такой операции может создать дубликат на сервере. Включайте, только если
    /// сервер поддерживает идемпотентность (например, по ключу идемпотентности).
    /// </summary>
    public bool RetryNonIdempotentMethods { get; set; }
}
