using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Tests.PriceCollection;

[TestFixture]
public sealed class WeeklyScheduleMarketSessionPolicyTests
{
    private static readonly ExchangeSession Nasdaq = new(
        DateTimeZoneProviders.Tzdb["America/New_York"],
        false,
        [Weekdays(new LocalTime(9, 30), new LocalTime(16, 0))]);

    private static readonly ExchangeSession Overnight = new(
        DateTimeZoneProviders.Tzdb["America/New_York"],
        false,
        [Weekdays(new LocalTime(22, 0), new LocalTime(6, 0))]);

    private static readonly ExchangeSession Crypto = new(null, true, []);

    [Test]
    public void GetStatus_WhenExchangeUnconfigured_ReturnsUnconfigured()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus status = policy.GetStatus(
            "XLON",
            Instant.FromUtc(2026, 10, 30, 14, 0));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Unconfigured));
    }

    [Test]
    public void GetStatus_WhenSessionHasNoZoneAndIsNotAlwaysOpen_ReturnsUnconfigured()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(
            new ExchangeSession(null, false, [Weekdays(new LocalTime(9, 30), new LocalTime(16, 0))]));

        ExchangeSessionStatus status = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 10, 30, 14, 0));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Unconfigured));
    }

    [Test]
    public void GetStatus_WhenLocalTimeIsExactlyOpen_ReturnsOpen()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus status = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 10, 30, 13, 30));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Open));
    }

    [Test]
    public void GetStatus_WhenLocalTimeIsOneSecondBeforeOpen_ReturnsClosed()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus status = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 10, 30, 13, 29, 59));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Closed));
    }

    [Test]
    public void GetStatus_WhenLocalTimeIsExactlyClose_ReturnsClosed()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus status = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 10, 30, 20, 0));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Closed));
    }

    [Test]
    public void GetStatus_WhenLocalTimeIsOneSecondBeforeClose_ReturnsOpen()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus status = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 10, 30, 19, 59, 59));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Open));
    }

    [Test]
    public void GetStatus_OnWeekend_ReturnsClosed()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus status = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 10, 31, 14, 0));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Closed));
    }

    [Test]
    public void GetStatus_AcrossDaylightSavingTransition_UsesZoneLocalTimes()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus beforeTransition = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 10, 30, 13, 30));
        ExchangeSessionStatus afterTransition = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 11, 2, 13, 30));
        ExchangeSessionStatus shiftedInstant = policy.GetStatus(
            "XNAS",
            Instant.FromUtc(2026, 11, 2, 14, 30));

        Assert.Multiple(() =>
        {
            Assert.That(beforeTransition, Is.EqualTo(ExchangeSessionStatus.Open));
            Assert.That(afterTransition, Is.EqualTo(ExchangeSessionStatus.Closed));
            Assert.That(shiftedInstant, Is.EqualTo(ExchangeSessionStatus.Open));
        });
    }

    [Test]
    public void GetStatus_WhenOvernightSessionCrossesMidnight_SpansBothDays()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Overnight);

        Assert.Multiple(() =>
        {
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 10, 31, 2, 0)),
                Is.EqualTo(ExchangeSessionStatus.Open));
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 10, 31, 9, 59)),
                Is.EqualTo(ExchangeSessionStatus.Open));
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 10, 31, 10, 0)),
                Is.EqualTo(ExchangeSessionStatus.Closed));
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 10, 30, 12, 0)),
                Is.EqualTo(ExchangeSessionStatus.Closed));
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 11, 2, 1, 0)),
                Is.EqualTo(ExchangeSessionStatus.Closed));
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 11, 3, 10, 59)),
                Is.EqualTo(ExchangeSessionStatus.Open));
        });
    }

    [Test]
    public void GetStatus_WhenAlwaysOpen_ReturnsOpen()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Crypto);

        Assert.Multiple(() =>
        {
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 11, 1, 4, 0)),
                Is.EqualTo(ExchangeSessionStatus.Open));
            Assert.That(
                policy.GetStatus("XNAS", Instant.FromUtc(2026, 10, 30, 4, 0)),
                Is.EqualTo(ExchangeSessionStatus.Open));
        });
    }

    [Test]
    public void GetStatus_WhenExchangeCaseDiffers_MatchesInsensitively()
    {
        WeeklyScheduleMarketSessionPolicy policy = Create(Nasdaq);

        ExchangeSessionStatus status = policy.GetStatus(
            "xnas",
            Instant.FromUtc(2026, 10, 30, 14, 0));

        Assert.That(status, Is.EqualTo(ExchangeSessionStatus.Open));
    }

    private static WeeklyScheduleMarketSessionPolicy Create(ExchangeSession session)
    {
        return new WeeklyScheduleMarketSessionPolicy(
            new Dictionary<string, ExchangeSession>(StringComparer.OrdinalIgnoreCase)
            {
                ["XNAS"] = session
            });
    }

    private static SessionWindow Weekdays(LocalTime open, LocalTime close)
    {
        return new SessionWindow(
            new HashSet<IsoDayOfWeek>
            {
                IsoDayOfWeek.Monday,
                IsoDayOfWeek.Tuesday,
                IsoDayOfWeek.Wednesday,
                IsoDayOfWeek.Thursday,
                IsoDayOfWeek.Friday
            },
            open,
            close);
    }
}
