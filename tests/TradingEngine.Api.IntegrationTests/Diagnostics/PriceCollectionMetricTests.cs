using System.Diagnostics.Metrics;
using NodaTime;
using TradingEngine.Api.PriceCollection;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class PriceCollectionMetricTests
{
    [Test]
    public void RecordRun_PartialRun_ExportsCountsSizesDurationAndDeadlineWithoutIdentities()
    {
        Dictionary<string, long> counts = [];
        List<int> sizes = [];
        List<double> durations = [];
        List<string> tagKeys = [];
        using MeterListener listener = new()
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == PriceCollectionTelemetry.MeterName)
                {
                    current.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string key = instrument.Name;
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                tagKeys.Add(tag.Key);
                key += ":" + tag.Value;
            }

            counts[key] = counts.GetValueOrDefault(key) + value;
        });
        listener.SetMeasurementEventCallback<int>((_, value, _, _) => sizes.Add(value));
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Add(value));
        listener.Start();
        using PriceCollectionTelemetry telemetry = new();
        Instant start = Instant.FromUtc(2026, 1, 5, 12, 0);
        PriceCollectionRunResult result = new(Guid.NewGuid(), start, start.Plus(Duration.FromSeconds(12)), true, [])
        {
            Diagnostics = new PriceCollectionRunDiagnostics
            {
                Eligible = 120, Requested = 120, Returned = 119, Persisted = 118,
                Deferred = 1, Failed = 1, Unprocessed = 3, ExpiredPendingSlots = 2,
                Requests = new MarketDataRequestStatistics(2, new Dictionary<int, long> { [120] = 2, [1] = 1 })
            }
        };

        telemetry.RecordRun(result);

        Assert.Multiple(() =>
        {
            Assert.That(counts["tradingengine.price_collection.instruments:persisted"], Is.EqualTo(118));
            Assert.That(counts["tradingengine.price_collection.instruments:deferred"], Is.EqualTo(1));
            Assert.That(counts["tradingengine.price_collection.instruments:unprocessed"], Is.EqualTo(3));
            Assert.That(counts["tradingengine.price_collection.requests:rates"], Is.EqualTo(3));
            Assert.That(counts["tradingengine.price_collection.requests:search"], Is.EqualTo(2));
            Assert.That(counts["tradingengine.price_collection.deadlines"], Is.EqualTo(1));
            Assert.That(counts["tradingengine.price_collection.expired_pending_slots"], Is.EqualTo(2));
            Assert.That(sizes, Is.EquivalentTo(new[] { 120, 120, 1 }));
            Assert.That(durations, Is.EqualTo(new[] { 12d }));
            Assert.That(tagKeys.Distinct(), Is.EquivalentTo(new[] { "outcome", "kind" }));
        });
    }
}
