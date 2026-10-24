using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Журнал, который складывает сообщения в список для проверок.</summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<LogEntry> _entries;

    public RecordingLogger(List<LogEntry> entries) => _entries = entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
}
