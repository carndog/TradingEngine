using NodaTime;
using TradingEngine.Domain;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Domain.Tests.Instruments;

[TestFixture]
public sealed class WatchedInstrumentTests
{
    private static readonly Instant CreatedAt = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public void Create_WithValidValues_ReturnsConfiguredInstrument()
    {
        WatchedInstrument instrument = CreateInstrument();

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Id.Value, Is.EqualTo(Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0")));
            Assert.That(instrument.Symbol.Value, Is.EqualTo("DEMO-1"));
            Assert.That(instrument.Exchange.Value, Is.EqualTo("XTEST"));
            Assert.That(instrument.QuoteCurrency.Value, Is.EqualTo("GBP"));
            Assert.That(instrument.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(instrument.SamplingPolicy, Is.EqualTo(SamplingPolicy.Standard));
            Assert.That(instrument.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(CreatedAt));
        });
    }

    [Test]
    public void StartMonitoring_WhenConfigured_ChangesStatePolicyAndTimestamp()
    {
        WatchedInstrument instrument = CreateInstrument();
        Instant changedAt = CreatedAt + Duration.FromMinutes(5);

        instrument.StartMonitoring(SamplingPolicy.Conservative, changedAt);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
            Assert.That(instrument.SamplingPolicy, Is.EqualTo(SamplingPolicy.Conservative));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(changedAt));
        });
    }

    [Test]
    public void StartMonitoring_WhenAlreadyMonitored_ThrowsDomainRuleViolationException()
    {
        WatchedInstrument instrument = CreateInstrument();
        instrument.StartMonitoring(SamplingPolicy.Standard, CreatedAt);

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => instrument.StartMonitoring(SamplingPolicy.Frequent, CreatedAt));

        Assert.That(exception!.Message, Does.Contain("already"));
    }

    [Test]
    public void ChangeSamplingPolicy_WhenTimestampPrecedesLatestChange_ThrowsDomainRuleViolationException()
    {
        WatchedInstrument instrument = CreateInstrument();

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => instrument.ChangeSamplingPolicy(
                SamplingPolicy.Frequent,
                CreatedAt - Duration.FromNanoseconds(1)));

        Assert.That(exception!.Message, Does.Contain("before"));
    }

    [Test]
    public void StopMonitoring_WhenMonitored_ReturnsConfiguredState()
    {
        WatchedInstrument instrument = CreateInstrument();
        Instant startedAt = CreatedAt + Duration.FromMinutes(1);
        Instant stoppedAt = startedAt + Duration.FromMinutes(1);
        instrument.StartMonitoring(SamplingPolicy.Standard, startedAt);

        instrument.StopMonitoring(stoppedAt);

        Assert.Multiple(() =>
        {
            Assert.That(instrument.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(stoppedAt));
        });
    }

    [Test]
    public void Create_WithUnknownSamplingPolicy_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WatchedInstrument.Create(
                WatchedInstrumentId.From(Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0")),
                InstrumentSymbol.From("DEMO-1"),
                ExchangeCode.From("XTEST"),
                QuoteCurrencyCode.From("GBP"),
                (SamplingPolicy)999,
                CreatedAt));
    }

    private static WatchedInstrument CreateInstrument()
    {
        return WatchedInstrument.Create(
            WatchedInstrumentId.From(Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0")),
            InstrumentSymbol.From("demo-1"),
            ExchangeCode.From("xtest"),
            QuoteCurrencyCode.From("gbp"),
            SamplingPolicy.Standard,
            CreatedAt);
    }
}
