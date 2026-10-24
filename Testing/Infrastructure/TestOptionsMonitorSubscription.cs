using System;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Подписка на изменение параметров в <see cref="TestOptionsMonitor{T}"/>.</summary>
internal sealed class TestOptionsMonitorSubscription<T> : IDisposable
    where T : class
{
    private readonly Action<T, string?> _listener;
    private readonly System.Collections.Generic.List<Action<T, string?>> _listeners;

    public TestOptionsMonitorSubscription(
        System.Collections.Generic.List<Action<T, string?>> listeners,
        Action<T, string?> listener)
    {
        _listeners = listeners;
        _listener = listener;
    }

    public void Dispose() => _listeners.Remove(_listener);
}
