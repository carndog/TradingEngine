using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Api.PriceCollection;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Infrastructure.MarketSessions;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class ExchangeCalendarRegistrationTests
{
    [Test]
    public void AddPriceCollection_PackagedCalendars_ResolvesThroughProductionRegistrationAfterRestart()
    {
        IConfigurationRoot configuration = Configuration();
        for (int restart = 0; restart < 2; restart++)
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddPriceCollection(configuration);
            using ServiceProvider provider = services.BuildServiceProvider();
            IMarketSessionPolicy policy = provider.GetRequiredService<IMarketSessionPolicy>();

            MarketSessionDecision london = policy.GetDecision("XLON", Instant.FromUtc(2026, 12, 25, 10, 0));
            MarketSessionDecision nasdaq = policy.GetDecision("XNAS", Instant.FromUtc(2026, 11, 26, 15, 0));
            MarketSessionDecision continuous = policy.GetDecision("XCONT", Instant.FromUtc(2027, 1, 1, 0, 0));

            Assert.That(provider.GetRequiredService<IExchangeTradingCalendar>(), Is.TypeOf<ConfiguredExchangeTradingCalendar>());
            Assert.That(london.ReasonCode, Is.EqualTo("price_collection.holiday"));
            Assert.That(nasdaq.ReasonCode, Is.EqualTo("price_collection.holiday"));
            Assert.That(london.CalendarVersion, Is.EqualTo("xlon-2026-11-16-v1"));
            Assert.That(continuous.Status, Is.EqualTo(ExchangeSessionStatus.Open));
            Assert.That(policy.GetStatus("XNAS", Instant.FromUtc(2027, 1, 4, 15, 0)), Is.EqualTo(ExchangeSessionStatus.CalendarUnavailable));
        }
    }

    private static IConfigurationRoot Configuration()
    {
        Dictionary<string, string?> values = new()
        {
            ["PriceCollection:Enabled"] = "false",
            ["PriceCollection:ExchangeSessions:XLON:TimeZone"] = "Europe/London",
            ["PriceCollection:ExchangeSessions:XLON:Windows:0:Open"] = "08:00",
            ["PriceCollection:ExchangeSessions:XLON:Windows:0:Close"] = "16:30",
            ["PriceCollection:ExchangeSessions:XNAS:TimeZone"] = "America/New_York",
            ["PriceCollection:ExchangeSessions:XNAS:Windows:0:Open"] = "09:30",
            ["PriceCollection:ExchangeSessions:XNAS:Windows:0:Close"] = "16:00",
            ["PriceCollection:ExchangeSessions:XCONT:AlwaysOpen"] = "true"
        };
        string[] days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
        for (int index = 0; index < days.Length; index++)
        {
            values[$"PriceCollection:ExchangeSessions:XLON:Windows:0:Days:{index}"] = days[index];
            values[$"PriceCollection:ExchangeSessions:XNAS:Windows:0:Days:{index}"] = days[index];
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
