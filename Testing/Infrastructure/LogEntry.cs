using Microsoft.Extensions.Logging;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Одна запись журнала: уровень и отформатированное сообщение.</summary>
internal sealed class LogEntry
{
    public LogEntry(LogLevel level, string message)
    {
        Level = level;
        Message = message;
    }

    public LogLevel Level { get; }

    public string Message { get; }
}
