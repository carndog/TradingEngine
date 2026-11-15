using System.Diagnostics.Metrics;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Api.PriceCollection;

internal sealed class PriceCollectionTelemetry : IDisposable
{
    internal const string MeterName = "TradingEngine.Api.PriceCollection";
    internal const string FailuresCounterName = "tradingengine.price_collection.failures";

    private readonly Meter _meter;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _instruments;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _deadlines;
    private readonly Counter<long> _expiredSlots;
    private readonly Histogram<double> _duration;
    private readonly Histogram<int> _batchSize;

    public PriceCollectionTelemetry()
    {
        _meter = new Meter(MeterName);
        _instruments = _meter.CreateCounter<long>("tradingengine.price_collection.instruments");
        _requests = _meter.CreateCounter<long>("tradingengine.price_collection.requests");
        _deadlines = _meter.CreateCounter<long>("tradingengine.price_collection.deadlines");
        _expiredSlots = _meter.CreateCounter<long>("tradingengine.price_collection.expired_pending_slots");
        _duration = _meter.CreateHistogram<double>("tradingengine.price_collection.duration", "s");
        _batchSize = _meter.CreateHistogram<int>("tradingengine.price_collection.batch_size", "{instrument}");
        _failures = _meter.CreateCounter<long>(
            FailuresCounterName,
            description: "Price collection attempts that ended in a Failed outcome.");
    }

    internal void RecordRun(PriceCollectionRunResult result)
    {
        PriceCollectionRunDiagnostics diagnostics = result.Diagnostics;
        RecordInstruments("eligible", diagnostics.Eligible);
        RecordInstruments("requested", diagnostics.Requested);
        RecordInstruments("returned", diagnostics.Returned);
        RecordInstruments("persisted", diagnostics.Persisted);
        RecordInstruments("deferred", diagnostics.Deferred);
        RecordInstruments("failed", diagnostics.Failed);
        RecordInstruments("unprocessed", diagnostics.Unprocessed);
        _requests.Add(diagnostics.Requests.RatesRequests, new KeyValuePair<string, object?>("kind", "rates"));
        _requests.Add(diagnostics.Requests.SearchRequests, new KeyValuePair<string, object?>("kind", "search"));
        _duration.Record((result.CompletedAt - result.StartedAt).TotalSeconds);
        _expiredSlots.Add(diagnostics.ExpiredPendingSlots);
        if (result.DeadlineReached)
        {
            _deadlines.Add(1);
        }

        foreach (KeyValuePair<int, long> batch in diagnostics.Requests.RateBatches)
        {
            for (long index = 0; index < batch.Value; index++)
            {
                _batchSize.Record(batch.Key);
            }
        }
    }

    private void RecordInstruments(string outcome, int count)
    {
        _instruments.Add(count, new KeyValuePair<string, object?>("outcome", outcome));
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
