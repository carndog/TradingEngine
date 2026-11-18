using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Signals;

namespace TradingEngine.Application.Tests.Signals;

[TestFixture]
public sealed class SignalFreshnessClassifierTests : IMarketSessionPolicy
{
    [TestCase(15, SignalFreshness.Current)]
    [TestCase(30, SignalFreshness.StaleLastKnown)]
    public void Classify_ContinuousMarket_UsesCadenceWithOneIntervalGrace(int minutes, SignalFreshness expected)
    {
        Instant cutoff = Instant.FromUtc(2026, 1, 12, 12, 0);
        SignalEvidence signal = ReadSignalsHandlerTests.Evidence();
        signal = signal with { Quote = signal.Quote! with { ScheduledFor = cutoff.Minus(Duration.FromMinutes(minutes)), RetrievedAt = cutoff.Minus(Duration.FromMinutes(minutes)) } };
        SignalFreshnessClassifier classifier = new(new WeeklyScheduleMarketSessionPolicy(new Dictionary<string, ExchangeSession> { ["XTEST"] = new(null, true, []) }));

        Assert.That(classifier.Classify(signal, cutoff), Is.EqualTo(expected));
    }

    [Test]
    public void Classify_WeekendWithFridayClosingQuote_IsLatestSession()
    {
        Instant friday = Instant.FromUtc(2026, 1, 9, 16, 45);
        SignalEvidence signal = ReadSignalsHandlerTests.Evidence();
        signal = signal with { Quote = signal.Quote! with { ScheduledFor = friday, RetrievedAt = friday.Plus(Duration.FromSeconds(5)) } };
        ExchangeSession session = new(DateTimeZoneProviders.Tzdb["Europe/London"], false,
            [new SessionWindow(new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday }, new LocalTime(9, 0), new LocalTime(17, 0))]);
        SignalFreshnessClassifier classifier = new(new WeeklyScheduleMarketSessionPolicy(new Dictionary<string, ExchangeSession> { ["XTEST"] = session }));

        Assert.That(classifier.Classify(signal, Instant.FromUtc(2026, 1, 11, 18, 0)), Is.EqualTo(SignalFreshness.LatestSession));
    }

    [Test]
    public void Classify_UnconfiguredExchange_DoesNotClaimFreshness()
    {
        SignalFreshnessClassifier classifier = new(new WeeklyScheduleMarketSessionPolicy(new Dictionary<string, ExchangeSession>()));
        Assert.That(classifier.Classify(ReadSignalsHandlerTests.Evidence(), Instant.FromUtc(2026, 1, 12, 12, 0)), Is.EqualTo(SignalFreshness.UnknownCalendar));
    }

    [Test]
    public void Classify_ProviderBudgetChangesEffectiveCadence_ReportsUnknownRatherThanFalseStaleness()
    {
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession> { ["XTEST"] = new(null, true, []) });
        SignalFreshnessClassifier classifier = new(sessions, new PriceCollectionOptions { SamplingPolicy = SamplingPolicyNames.ProviderBudgetClamped });
        Assert.That(classifier.Classify(ReadSignalsHandlerTests.Evidence(), Instant.FromUtc(2026, 1, 12, 12, 0)), Is.EqualTo(SignalFreshness.UnknownCadence));
    }

    [TestCase(45, SignalFreshness.LatestSession)]
    [TestCase(30, SignalFreshness.StaleLastKnown)]
    public void Classify_KnownHoliday_UsesLastExpectedOpenSession(int minute, SignalFreshness expected)
    {
        Instant thursday = Instant.FromUtc(2026, 1, 15, 16, minute);
        SignalEvidence signal = ReadSignalsHandlerTests.Evidence();
        signal = signal with { Quote = signal.Quote! with { ScheduledFor = thursday, RetrievedAt = thursday } };
        Assert.That(new SignalFreshnessClassifier(this).Classify(signal, Instant.FromUtc(2026, 1, 16, 18, 0)), Is.EqualTo(expected));
    }

    [Test]
    public void Classify_MissingQuote_ReturnsMissing()
    {
        SignalEvidence signal = ReadSignalsHandlerTests.Evidence() with { Quote = null };
        SignalFreshness result = new SignalFreshnessClassifier(this).Classify(signal, Instant.FromUtc(2026, 1, 12, 12, 0));
        Assert.That(result, Is.EqualTo(SignalFreshness.Missing));
    }

    [Test]
    public void Classify_NoOpenSlotWithinLookback_ReturnsUnknownExpectedCollection()
    {
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession> { ["XTEST"] = new(DateTimeZone.Utc, false, []) });
        SignalFreshness result = new SignalFreshnessClassifier(sessions).Classify(ReadSignalsHandlerTests.Evidence(), Instant.FromUtc(2026, 1, 12, 12, 0));
        Assert.That(result, Is.EqualTo(SignalFreshness.UnknownExpectedCollection));
    }

    public ExchangeSessionStatus GetStatus(string exchange, Instant at)
    {
        ZonedDateTime local = at.InZone(DateTimeZoneProviders.Tzdb["Europe/London"]);
        if (local.Date == new LocalDate(2026, 1, 16) || local.DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
        {
            return ExchangeSessionStatus.Closed;
        }

        return local.TimeOfDay >= new LocalTime(9, 0) && local.TimeOfDay < new LocalTime(17, 0)
            ? ExchangeSessionStatus.Open : ExchangeSessionStatus.Closed;
    }
}
