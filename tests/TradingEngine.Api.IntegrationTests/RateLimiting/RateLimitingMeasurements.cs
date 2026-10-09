using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TradingEngine.Api.RateLimiting;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

internal sealed class RateLimitingMeasurements : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentQueue<IReadOnlyList<KeyValuePair<string, object?>>> _measurements = new();

    public RateLimitingMeasurements()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RateLimitingTelemetry.MeterName
                    && instrument.Name == RateLimitingTelemetry.CounterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>(Record);
        _listener.Start();
    }

    public int Count(string policy, string outcome, string limit)
    {
        return _measurements.Count(tags =>
            TagValue(tags, "policy") == policy
            && TagValue(tags, "outcome") == outcome
            && TagValue(tags, "limit") == limit);
    }

    public IEnumerable<IReadOnlyList<KeyValuePair<string, object?>>> All()
    {
        return _measurements.ToArray();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void Record(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        _measurements.Enqueue(tags.ToArray());
    }

    private static string? TagValue(
        IReadOnlyList<KeyValuePair<string, object?>> tags,
        string name)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == name)
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }
}
