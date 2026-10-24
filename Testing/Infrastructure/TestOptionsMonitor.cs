using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>
/// Управляемый <see cref="IOptionsMonitor{TOptions}"/> для модульных тестов обработчика:
/// позволяет подменить параметры и уведомить подписчиков, как это делает конфигурация.
/// </summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    private readonly List<Action<T, string?>> _listeners = [];
    private T _value;

    public TestOptionsMonitor(T value)
    {
        _value = value;
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public void Update(T value)
    {
        _value = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener(value, null);
        }
    }

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listeners.Add(listener);
        return new TestOptionsMonitorSubscription<T>(_listeners, listener);
    }
}
