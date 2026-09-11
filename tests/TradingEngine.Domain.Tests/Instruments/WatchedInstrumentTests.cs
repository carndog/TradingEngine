using NodaTime;
using TradingEngine.Domain;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Domain.Tests.Instruments;

[TestFixture]
public sealed class WatchedInstrumentTests
{
    private static readonly Instant CreatedAt = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public void Create_establishes_a_valid_configured_instrument()
    {
        WatchedInstrument instrument = CreateInstrument();

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Id.Value, Is.EqualTo(Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0")));
            Assert.That(instrument.BrokerCode.Value, Is.EqualTo("DEMO-1"));
            Assert.That(instrument.Exchange.Value, Is.EqualTo("XTEST"));
            Assert.That(instrument.Currency.Value, Is.EqualTo("GBP"));
            Assert.That(instrument.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(instrument.SamplingPolicy, Is.EqualTo(SamplingPolicy.Standard));
            Assert.That(instrument.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(CreatedAt));
        });
    }

    [Test]
    public void StartMonitoring_changes_state_policy_and_timestamp()
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
    public void StartMonitoring_rejects_a_duplicate_transition()
    {
        WatchedInstrument instrument = CreateInstrument();
        instrument.StartMonitoring(SamplingPolicy.Standard, CreatedAt);

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => instrument.StartMonitoring(SamplingPolicy.Frequent, CreatedAt));

        Assert.That(exception!.Message, Does.Contain("already"));
    }

    [Test]
    public void ChangeSamplingPolicy_rejects_a_timestamp_before_the_latest_change()
    {
        WatchedInstrument instrument = CreateInstrument();

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => instrument.ChangeSamplingPolicy(
                SamplingPolicy.Frequent,
                CreatedAt - Duration.FromNanoseconds(1)));

        Assert.That(exception!.Message, Does.Contain("before"));
    }

    [Test]
    public void StopMonitoring_returns_the_instrument_to_configured_state()
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
    public void Create_rejects_an_unknown_sampling_policy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WatchedInstrument.Create(
                WatchedInstrumentId.From(Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0")),
                BrokerInstrumentCode.From("DEMO-1"),
                ExchangeCode.From("XTEST"),
                CurrencyCode.From("GBP"),
                (SamplingPolicy)999,
                CreatedAt));
    }

    private static WatchedInstrument CreateInstrument()
    {
        return WatchedInstrument.Create(
            WatchedInstrumentId.From(Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0")),
            BrokerInstrumentCode.From("demo-1"),
            ExchangeCode.From("xtest"),
            CurrencyCode.From("gbp"),
            SamplingPolicy.Standard,
            CreatedAt);
    }
}
