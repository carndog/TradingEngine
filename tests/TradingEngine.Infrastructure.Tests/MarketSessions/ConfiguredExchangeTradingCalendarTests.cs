using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NodaTime.Text;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Infrastructure.MarketSessions;

namespace TradingEngine.Infrastructure.Tests.MarketSessions;

[TestFixture]
public sealed class ConfiguredExchangeTradingCalendarTests
{
    [TestCase("XLON", "2026-12-25", TradingDayKind.Closed)]
    [TestCase("XLON", "2026-12-28", TradingDayKind.Closed)]
    [TestCase("XLON", "2027-03-26", TradingDayKind.Closed)]
    [TestCase("XLON", "2027-03-29", TradingDayKind.Closed)]
    [TestCase("XLON", "2027-12-27", TradingDayKind.Closed)]
    [TestCase("XLON", "2027-12-28", TradingDayKind.Closed)]
    [TestCase("XLON", "2026-12-24", TradingDayKind.Replacement)]
    [TestCase("XLON", "2027-12-31", TradingDayKind.Replacement)]
    [TestCase("XNAS", "2026-11-26", TradingDayKind.Closed)]
    [TestCase("XNAS", "2026-07-03", TradingDayKind.Closed)]
    [TestCase("XNAS", "2026-11-27", TradingDayKind.Replacement)]
    [TestCase("XNAS", "2026-12-24", TradingDayKind.Replacement)]
    [TestCase("XNAS", "2026-11-11", TradingDayKind.Normal)]
    [TestCase("XNAS", "2027-01-04", TradingDayKind.Unavailable)]
    [TestCase("XLON", "2028-01-04", TradingDayKind.Unavailable)]
    [TestCase("XNYS", "2026-11-16", TradingDayKind.Unavailable)]
    public void GetDay_PackagedCalendar_ReturnsVerifiedExchangeSpecificSchedule(string exchange, string date, TradingDayKind expected)
    {
        using ConfiguredExchangeTradingCalendar calendar = Calendar(new ConfigurationBuilder().Build());

        ExchangeTradingDay day = calendar.GetDay(exchange, LocalDatePattern.Iso.Parse(date).Value);

        Assert.That(day.Kind, Is.EqualTo(expected));
    }

    [TestCase("XLON", "2026-12-24T12:29:59Z", ExchangeSessionStatus.Open)]
    [TestCase("XLON", "2026-12-24T12:30:00Z", ExchangeSessionStatus.Closed)]
    [TestCase("XLON", "2026-12-24T12:30:01Z", ExchangeSessionStatus.Closed)]
    [TestCase("XNAS", "2026-11-27T17:59:59Z", ExchangeSessionStatus.Open)]
    [TestCase("XNAS", "2026-11-27T18:00:00Z", ExchangeSessionStatus.Closed)]
    [TestCase("XNAS", "2026-11-27T18:00:01Z", ExchangeSessionStatus.Closed)]
    [TestCase("XNAS", "2026-03-09T13:30:00Z", ExchangeSessionStatus.Open)]
    [TestCase("XNAS", "2026-03-06T13:30:00Z", ExchangeSessionStatus.Closed)]
    [TestCase("XNAS", "2026-10-26T13:30:00Z", ExchangeSessionStatus.Open)]
    [TestCase("XNAS", "2026-11-02T13:30:00Z", ExchangeSessionStatus.Closed)]
    [TestCase("XNAS", "2026-11-21T15:00:00Z", ExchangeSessionStatus.Closed)]
    [TestCase("XNAS", "2027-01-04T15:00:00Z", ExchangeSessionStatus.CalendarUnavailable)]
    [TestCase("XCONT", "2027-12-25T00:00:00Z", ExchangeSessionStatus.Open)]
    public void GetDecision_CalendarAndTimeZone_UsesCurrentSession(string exchange, string instant, ExchangeSessionStatus expected)
    {
        using ConfiguredExchangeTradingCalendar calendar = Calendar(new ConfigurationBuilder().Build());
        CalendarMarketSessionPolicy policy = new(Sessions(), calendar);

        MarketSessionDecision decision = policy.GetDecision(exchange, InstantPattern.ExtendedIso.Parse(instant).Value);

        Assert.That(decision.Status, Is.EqualTo(expected));
    }

    [Test]
    public void GetDecision_UtcDateDiffersFromNewYork_UsesExchangeLocalHolidayDate()
    {
        using ConfiguredExchangeTradingCalendar calendar = Calendar(new ConfigurationBuilder().Build());
        CalendarMarketSessionPolicy policy = new(Sessions(), calendar);

        MarketSessionDecision decision = policy.GetDecision("XNAS", Instant.FromUtc(2026, 11, 27, 0, 30));

        Assert.That(decision.ReasonCode, Is.EqualTo("price_collection.holiday"));
        Assert.That(decision.Description, Does.Contain("Thanksgiving"));
    }

    [Test]
    public void GetDecision_NewDayAfterHoliday_ResumesWithoutMidnightCallback()
    {
        using ConfiguredExchangeTradingCalendar calendar = Calendar(new ConfigurationBuilder().Build());
        CalendarMarketSessionPolicy policy = new(Sessions(), calendar);

        MarketSessionDecision holiday = policy.GetDecision("XNAS", Instant.FromUtc(2026, 11, 26, 15, 0));
        MarketSessionDecision nextDay = policy.GetDecision("XNAS", Instant.FromUtc(2026, 11, 27, 15, 0));

        Assert.That(holiday.Status, Is.EqualTo(ExchangeSessionStatus.Closed));
        Assert.That(nextDay.Status, Is.EqualTo(ExchangeSessionStatus.Open));
    }

    [Test]
    public void GetDay_CoverageNearExpiry_ReportsWarningAndVersion()
    {
        using ConfiguredExchangeTradingCalendar calendar = Calendar(new ConfigurationBuilder().Build());

        ExchangeTradingDay day = calendar.GetDay("XNAS", new LocalDate(2026, 11, 16));

        Assert.That(day.Notice, Does.Contain("2026-12-31"));
        Assert.That(day.Version, Is.EqualTo("xnas-2026-11-16-v1"));
    }

    [Test]
    public void GetDecision_ReloadSameDay_UsesReplacementVersionAndExceptionalClosure()
    {
        ExchangeCalendarOptions definition = Definition();
        IConfigurationRoot configuration = Configuration(definition);
        using ConfiguredExchangeTradingCalendar calendar = Calendar(configuration);
        CalendarMarketSessionPolicy policy = new(Sessions(), calendar);
        Instant at = Instant.FromUtc(2026, 11, 16, 10, 0);
        Assert.That(policy.GetStatus("XLON", at), Is.EqualTo(ExchangeSessionStatus.Open));
        definition.Version = "synthetic-v2";
        definition.Overrides.Add(new CalendarDayOptions { Date = "2026-11-16", Closed = true, Reason = "synthetic override" });

        configuration["TradingCalendars:DefinitionsJson"] = JsonSerializer.Serialize(new[] { definition });
        configuration.Reload();
        MarketSessionDecision decision = policy.GetDecision("XLON", at);

        Assert.That(decision.Status, Is.EqualTo(ExchangeSessionStatus.Closed));
        Assert.That(decision.ReasonCode, Is.EqualTo("price_collection.exceptional_closure"));
        Assert.That(decision.CalendarVersion, Is.EqualTo("synthetic-v2"));
    }

    [Test]
    public void GetDay_InvalidReload_RetainsVerifiedCoverageAndExposesFailure()
    {
        IConfigurationRoot configuration = Configuration(Definition());
        using ConfiguredExchangeTradingCalendar calendar = Calendar(configuration);
        configuration["TradingCalendars:DefinitionsJson"] = "invalid-json";

        configuration.Reload();
        ExchangeTradingDay covered = calendar.GetDay("XLON", new LocalDate(2026, 11, 16));
        ExchangeTradingDay expired = calendar.GetDay("XLON", new LocalDate(2027, 1, 1));

        Assert.That(covered.Kind, Is.EqualTo(TradingDayKind.Normal));
        Assert.That(covered.Notice, Does.Contain("refresh failed"));
        Assert.That(expired.Kind, Is.EqualTo(TradingDayKind.Unavailable));
    }

    [TestCase("duplicate")]
    [TestCase("invalid-date")]
    [TestCase("outside-coverage")]
    [TestCase("overlap")]
    [TestCase("closed-with-windows")]
    [TestCase("no-windows")]
    [TestCase("bad-time-zone")]
    [TestCase("reversed-window")]
    public void Constructor_InvalidConfiguration_RejectsDataset(string fault)
    {
        ExchangeCalendarOptions definition = Definition();
        CalendarDayOptions day = new() { Date = "2026-11-16", Closed = true, Reason = "synthetic" };
        definition.Exceptions.Add(day);
        switch (fault)
        {
            case "duplicate": definition.Exceptions.Add(day); break;
            case "invalid-date": day.Date = "2026-02-30"; break;
            case "outside-coverage": day.Date = "2027-01-01"; break;
            case "bad-time-zone": definition.TimeZone = "invalid"; break;
            case "no-windows": day.Closed = false; break;
            default:
                day.Closed = fault == "closed-with-windows";
                day.Windows.Add(new CalendarWindowOptions { Open = "08:00", Close = fault == "reversed-window" ? "07:00" : "12:00" });
                if (fault == "overlap")
                {
                    day.Windows.Add(new CalendarWindowOptions { Open = "11:00", Close = "13:00" });
                }
                break;
        }

        Assert.That(() => Calendar(Configuration(definition)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void GetDecision_WrongSessionTimeZone_BlocksCollection()
    {
        using ConfiguredExchangeTradingCalendar calendar = Calendar(new ConfigurationBuilder().Build());
        Dictionary<string, ExchangeSession> sessions = Sessions();
        sessions["XLON"] = sessions["XLON"] with { Zone = DateTimeZone.Utc };
        CalendarMarketSessionPolicy policy = new(sessions, calendar);

        MarketSessionDecision decision = policy.GetDecision("XLON", Instant.FromUtc(2026, 11, 16, 10, 0));

        Assert.That(decision.Status, Is.EqualTo(ExchangeSessionStatus.CalendarUnavailable));
    }

    private static ConfiguredExchangeTradingCalendar Calendar(IConfiguration configuration)
    {
        return new ConfiguredExchangeTradingCalendar(configuration, NullLogger<ConfiguredExchangeTradingCalendar>.Instance);
    }

    private static IConfigurationRoot Configuration(ExchangeCalendarOptions definition)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TradingCalendars:DefinitionsJson"] = JsonSerializer.Serialize(new[] { definition })
        }).Build();
    }

    private static ExchangeCalendarOptions Definition()
    {
        return new ExchangeCalendarOptions
        {
            Exchange = "XLON", Version = "synthetic-v1", TimeZone = "Europe/London",
            CoverageStart = "2026-01-01", CoverageEnd = "2026-12-31", VerifiedOn = "2026-11-16",
            Sources = ["https://example.test/calendar"]
        };
    }

    private static Dictionary<string, ExchangeSession> Sessions()
    {
        HashSet<IsoDayOfWeek> weekdays = [IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday,
            IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday];
        return new Dictionary<string, ExchangeSession>(StringComparer.OrdinalIgnoreCase)
        {
            ["XLON"] = new(DateTimeZoneProviders.Tzdb["Europe/London"], false,
                [new SessionWindow(weekdays, new LocalTime(8, 0), new LocalTime(16, 30))], true),
            ["XNAS"] = new(DateTimeZoneProviders.Tzdb["America/New_York"], false,
                [new SessionWindow(weekdays, new LocalTime(9, 30), new LocalTime(16, 0))], true),
            ["XCONT"] = new(null, true, [])
        };
    }
}
