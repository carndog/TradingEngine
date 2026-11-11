using System.Diagnostics.Metrics;

namespace TradingEngine.Api.PriceCollection;

internal sealed class PriceCollectionTelemetry : IDisposable
{
    internal const string MeterName = "TradingEngine.Api.PriceCollection";
    internal const string FailuresCounterName = "tradingengine.price_collection.failures";

    private readonly Meter _meter;
    private readonly Counter<long> _failures;

    public PriceCollectionTelemetry()
    {
        _meter = new Meter(MeterName);
        _failures = _meter.CreateCounter<long>(
            FailuresCounterName,
            description: "Price collection attempts that ended in a Failed outcome.");
    }

    internal void RecordFailures(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _failures.Add(count);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
