using Microsoft.Extensions.Logging;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<IReadOnlyList<KeyValuePair<string, object?>>> _states = [];

    public IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> States => _states;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            _states.Add(values);
        }
    }
}
