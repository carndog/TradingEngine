using NodaTime;
using TradingEngine.Application.Tests.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Read;

[TestFixture]
public sealed class GetWatchedInstrumentHandlerTests
{
    [Test]
    public async Task HandleAsync_WithKnownId_ReturnsStoredConfiguration()
    {
        WatchedInstrumentConfiguration configuration = CreateConfiguration();
        CapturingWatchedInstrumentStore store = new(
            CapturingWatchedInstrumentStore.DefaultGeneratedId,
            configuration);
        GetWatchedInstrumentHandler handler = new(store);
        GetWatchedInstrument query = new(configuration.Instrument.Id);

        Result<WatchedInstrumentConfiguration> result = await handler.HandleAsync(
            query,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.SameAs(configuration));
            Assert.That(store.RequestedInstrumentId, Is.EqualTo(query.InstrumentId));
        });
    }

    [Test]
    public async Task HandleAsync_WithUnknownId_ReturnsNotFound()
    {
        CapturingWatchedInstrumentStore store = new(
            CapturingWatchedInstrumentStore.DefaultGeneratedId,
            WatchedInstrumentErrors.ConfigurationNotFound);
        GetWatchedInstrumentHandler handler = new(store);
        GetWatchedInstrument query = new(Guid.Parse("a34b2207-fc21-4226-91b2-47eb4a40bde1"));

        Result<WatchedInstrumentConfiguration> result = await handler.HandleAsync(
            query,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ConfigurationNotFound));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        });
    }

    [Test]
    public async Task HandleAsync_WhenCancelled_PropagatesCancellation()
    {
        CapturingWatchedInstrumentStore store = new();
        GetWatchedInstrumentHandler handler = new(store);
        GetWatchedInstrument query = new(Guid.Parse("a34b2207-fc21-4226-91b2-47eb4a40bde1"));
        CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(query, cancellation.Token));
    }

    private static WatchedInstrumentConfiguration CreateConfiguration()
    {
        WatchedInstrument instrument = WatchedInstrument
            .Create(
                Guid.Parse("a34b2207-fc21-4226-91b2-47eb4a40bde1"),
                "demo-2",
                "xtest",
                "gbp",
                60,
                Instant.FromUtc(2026, 1, 2, 9, 30))
            .Value;

        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(
            4,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    95m,
                    100m,
                    105m,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value
                    ]).Value
            ],
            []).Value;

        return new WatchedInstrumentConfiguration(instrument, definition);
    }
}
