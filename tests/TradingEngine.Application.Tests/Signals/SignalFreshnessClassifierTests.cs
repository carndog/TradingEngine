using NodaTime;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Signals;

namespace TradingEngine.Application.Tests.Signals;

[TestFixture]
public sealed class SignalFreshnessClassifierTests
{
    [TestCase(15, "current")]
    [TestCase(30, "stale_last_known")]
    public void Classify_ContinuousMarket_UsesCadenceWithOneIntervalGrace(int minutes, string expected)
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

        Assert.That(classifier.Classify(signal, Instant.FromUtc(2026, 1, 11, 18, 0)), Is.EqualTo("latest_session"));
    }

    [Test]
    public void Classify_UnconfiguredExchange_DoesNotClaimFreshness()
    {
        SignalFreshnessClassifier classifier = new(new WeeklyScheduleMarketSessionPolicy(new Dictionary<string, ExchangeSession>()));
        Assert.That(classifier.Classify(ReadSignalsHandlerTests.Evidence(), Instant.FromUtc(2026, 1, 12, 12, 0)), Is.EqualTo("unknown_calendar"));
    }
}
