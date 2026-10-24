using System;
using System.Collections.Generic;

namespace Common.Helper.Http;

/// <summary>
/// Параметры типизированного HTTP-клиента.
/// Загружаются из конфигурации приложения (appsettings.json) и поддерживают горячую перезагрузку:
/// после изменения файла новые значения применяются к следующим запросам без перезапуска приложения.
/// </summary>
public sealed class ResilientHttpClientOptions
{
    /// <summary>
    /// Базовый адрес клиента. Допустимы только абсолютные адреса http/https.
    /// Применяется к запросам с относительным адресом; для абсолютных адресов игнорируется.
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// Общий таймаут запроса, включая все повторы.
    /// <see cref="TimeSpan.Zero"/> отключает ограничение. По умолчанию 30 секунд.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Заголовки по умолчанию. Заголовки конкретного запроса имеют приоритет над значениями по умолчанию.
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Параметры устойчивости: повторы, предохранитель и таймаут попытки.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();
}
