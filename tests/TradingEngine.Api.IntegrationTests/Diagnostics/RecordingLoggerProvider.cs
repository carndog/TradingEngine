using Microsoft.Extensions.Logging;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly RecordingLogger<object> _logger = new();

    public IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> States => _logger.States;

    public ILogger CreateLogger(string categoryName)
    {
        return _logger;
    }

    public void Dispose()
    {
    }
}
