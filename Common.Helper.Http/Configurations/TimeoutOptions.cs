using System;

namespace Common.Helper.Http;

/// <summary>Параметры таймаута.</summary>
public sealed class TimeoutOptions
{
    /// <summary>Включает таймаут.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Длительность таймаута: от 10 мс до 1 суток, если таймаут включён.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
