using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

[TestFixture]
public sealed class RegisterWatchedInstrumentHandlerTests
{
    [Test]
    public async Task HandleAsync_WithValidCommand_UsesClockAndStoresInstrument()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        RegisterWatchedInstrument command = new(
            WatchedInstrumentId.From(Guid.Parse("a34b2207-fc21-4226-91b2-47eb4a40bde1")),
            InstrumentSymbol.From("demo-2"),
            ExchangeCode.From("xtest"),
            QuoteCurrencyCode.From("gbp"),
            SamplingPolicy.Conservative);

        WatchedInstrument result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(store.AddedInstrument));
            Assert.That(result.CreatedAt, Is.EqualTo(now));
            Assert.That(result.LastChangedAt, Is.EqualTo(now));
            Assert.That(result.MonitoringState, Is.EqualTo(MonitoringState.Configured));
        });
    }

}
