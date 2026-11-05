using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class EToroMarketDataRegistrationTests
{
    [Test]
    public void AddEtoroMarketData_WithConfiguration_ResolvesMarketDataProvider()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["EToro:MarketData:ApiKey"] = "test-api-key",
            ["EToro:MarketData:UserKey"] = "test-user-key"
        });

        IMarketDataProvider marketData = provider.GetRequiredService<IMarketDataProvider>();

        Assert.That(marketData, Is.TypeOf<EToroMarketDataProvider>());
    }

    [Test]
    public void AddEtoroMarketData_Always_RegistersSharedCollaborators()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["EToro:MarketData:ApiKey"] = "test-api-key",
            ["EToro:MarketData:UserKey"] = "test-user-key"
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                provider.GetRequiredService<ProviderRateGate>(),
                Is.SameAs(provider.GetRequiredService<ProviderRateGate>()));
            Assert.That(
                provider.GetRequiredService<EToroInstrumentIdCache>(),
                Is.SameAs(provider.GetRequiredService<EToroInstrumentIdCache>()));
            Assert.That(provider.GetRequiredService<TimeProvider>(),
                Is.SameAs(TimeProvider.System));
            Assert.That(provider.GetRequiredService<IClock>(),
                Is.SameAs(SystemClock.Instance));
        });
    }

    [Test]
    public void AddEtoroMarketData_WithSearchPageSizeOutsideBounds_FailsStartupValidation(
        [Values(0, 101)] int searchPageSize)
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["EToro:MarketData:ApiKey"] = "test-api-key",
            ["EToro:MarketData:UserKey"] = "test-user-key",
            ["EToro:MarketData:SearchPageSize"] = searchPageSize.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        });

        IStartupValidator validator = provider
            .GetServices<IStartupValidator>()
            .Single();

        Assert.That(
            () => validator.Validate(),
            Throws.TypeOf<OptionsValidationException>());
    }

    [Test]
    public async Task AddEtoroMarketData_WithValidSearchPageSize_SendsConfiguredPageSize()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "10.5", "10.6", "2026-10-30T14:31:12Z", "realtime")));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EToro:MarketData:ApiKey"] = "test-api-key",
                ["EToro:MarketData:UserKey"] = "test-user-key",
                ["EToro:MarketData:SearchPageSize"] = "40",
                ["EToro:MarketData:ExchangeMap:XTEST:0"] = "Test Exchange",
                ["EToro:MarketData:QuoteCurrencyMap:XTEST:DEMO"] = "USD"
            })
            .Build();
        ServiceCollection services = new();
        FakeClock clock = new(Instant.FromUtc(2026, 10, 30, 12, 0, 0));
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<TimeProvider>(
            new ManualTimeProvider(
                delay => clock.Advance(Duration.FromTimeSpan(delay))));
        services.AddEtoroMarketData(configuration);
        services.ConfigureHttpClientDefaults(
            builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        using ServiceProvider provider = services.BuildServiceProvider();

        IMarketDataProvider marketData = provider
            .GetRequiredService<IMarketDataProvider>();
        Result<MarketQuote> result = await marketData.GetCurrentQuoteAsync(
            new MarketInstrumentIdentity("XTEST", "DEMO", "USD"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                handler.Requests[0].RequestUri!.ToString(),
                Does.Contain("pageSize=40"));
        });
    }

    private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new();
        services.AddEtoroMarketData(configuration);
        return services.BuildServiceProvider();
    }
}
