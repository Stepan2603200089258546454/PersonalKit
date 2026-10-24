using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>Фабрика журналов, отдающая <see cref="RecordingLogger"/> для любой категории.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly RecordingLogger _logger;

    public RecordingLoggerFactory(List<LogEntry> entries) => _logger = new RecordingLogger(entries);

    public ILogger CreateLogger(string categoryName) => _logger;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
