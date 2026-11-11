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

    [Test]
    public async Task AddEtoroMarketData_WithJsonQuoteCurrencyMap_PreservesSymbolIdentities()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4242, "DEMO.X", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4242, "10.5", "10.6", "2026-10-30T14:31:12Z", "realtime")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4343, "DEMO_X", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4343, "20.5", "20.6", "2026-10-30T14:31:12Z", "realtime")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4545, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4545, "30.5", "30.6", "2026-10-30T14:31:12Z", "realtime")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.SearchBody(
            (4646, "LOCAL", 5, "Test Exchange")));
        handler.EnqueueResponse(HttpStatusCode.OK, EToroPayloads.RatesBody(
            (4646, "40.5", "40.6", "2026-10-30T14:31:12Z", "realtime")));
        using ServiceProvider provider = BuildQuoteProvider(
            handler,
            new Dictionary<string, string?>
            {
                ["EToro:MarketData:ApiKey"] = "test-api-key",
                ["EToro:MarketData:UserKey"] = "test-user-key",
                ["EToro:MarketData:ExchangeMap:XTEST:0"] = "Test Exchange",
                ["EToro:MarketData:QuoteCurrencyMap:XTEST:LOCAL"] = "CHF",
                ["EToro:MarketData:QuoteCurrencyMapJson"] =
                    "{\"XTEST\":{\"DEMO.X\":\"GBX\",\"DEMO_X\":\"USD\",\"DEMO\":\"EUR\"}}"
            });
        IMarketDataProvider marketData = provider
            .GetRequiredService<IMarketDataProvider>();

        Result<MarketQuote> dotted = await marketData.GetCurrentQuoteAsync(
            new MarketInstrumentIdentity("XTEST", "DEMO.X", "GBX"),
            CancellationToken.None);
        Result<MarketQuote> underscored = await marketData.GetCurrentQuoteAsync(
            new MarketInstrumentIdentity("XTEST", "DEMO_X", "USD"),
            CancellationToken.None);
        Result<MarketQuote> plain = await marketData.GetCurrentQuoteAsync(
            new MarketInstrumentIdentity("XTEST", "DEMO", "EUR"),
            CancellationToken.None);
        Result<MarketQuote> local = await marketData.GetCurrentQuoteAsync(
            new MarketInstrumentIdentity("XTEST", "LOCAL", "CHF"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dotted.IsSuccess, Is.True);
            Assert.That(dotted.Value.ProviderInstrumentId, Is.EqualTo(4242));
            Assert.That(underscored.IsSuccess, Is.True);
            Assert.That(underscored.Value.ProviderInstrumentId, Is.EqualTo(4343));
            Assert.That(plain.IsSuccess, Is.True);
            Assert.That(plain.Value.ProviderInstrumentId, Is.EqualTo(4545));
            Assert.That(local.IsSuccess, Is.True);
            Assert.That(local.Value.ProviderInstrumentId, Is.EqualTo(4646));
            Assert.That(handler.Requests, Has.Count.EqualTo(8));
            Assert.That(
                handler.Requests[0].RequestUri!.ToString(),
                Does.Contain("internalSymbolFull=DEMO.X"));
            Assert.That(
                handler.Requests[2].RequestUri!.ToString(),
                Does.Contain("internalSymbolFull=DEMO_X"));
            Assert.That(
                handler.Requests[4].RequestUri!.ToString(),
                Does.Contain("internalSymbolFull=DEMO&"));
            Assert.That(
                handler.Requests[6].RequestUri!.ToString(),
                Does.Contain("internalSymbolFull=LOCAL"));
        });
    }

    [Test]
    public async Task AddEtoroMarketData_WithJsonQuoteCurrencyMapMissingSymbol_FailsBeforeHttp()
    {
        ScriptedHttpMessageHandler handler = new();
        using ServiceProvider provider = BuildQuoteProvider(
            handler,
            new Dictionary<string, string?>
            {
                ["EToro:MarketData:ApiKey"] = "test-api-key",
                ["EToro:MarketData:UserKey"] = "test-user-key",
                ["EToro:MarketData:ExchangeMap:XTEST:0"] = "Test Exchange",
                ["EToro:MarketData:QuoteCurrencyMapJson"] =
                    "{\"XTEST\":{\"DEMO.X\":\"GBX\"}}"
            });
        IMarketDataProvider marketData = provider
            .GetRequiredService<IMarketDataProvider>();

        Result<MarketQuote> result = await marketData.GetCurrentQuoteAsync(
            new MarketInstrumentIdentity("XTEST", "DEMO_X", "USD"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo("market_data.quote_currency_unconfigured"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task AddEtoroMarketData_WithJsonQuoteCurrencyMapConflict_FailsBeforeHttp()
    {
        ScriptedHttpMessageHandler handler = new();
        using ServiceProvider provider = BuildQuoteProvider(
            handler,
            new Dictionary<string, string?>
            {
                ["EToro:MarketData:ApiKey"] = "test-api-key",
                ["EToro:MarketData:UserKey"] = "test-user-key",
                ["EToro:MarketData:ExchangeMap:XTEST:0"] = "Test Exchange",
                ["EToro:MarketData:QuoteCurrencyMapJson"] =
                    "{\"XTEST\":{\"DEMO.X\":\"GBX\"}}"
            });
        IMarketDataProvider marketData = provider
            .GetRequiredService<IMarketDataProvider>();

        Result<MarketQuote> result = await marketData.GetCurrentQuoteAsync(
            new MarketInstrumentIdentity("XTEST", "DEMO.X", "USD"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo("market_data.quote_currency_conflict"));
            Assert.That(
                result.Error.Extensions?["providerCurrency"],
                Is.EqualTo("GBX"));
            Assert.That(
                result.Error.Extensions?["requestedCurrency"],
                Is.EqualTo("USD"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void AddEtoroMarketData_WithMalformedQuoteCurrencyMapJson_FailsOptionsEvaluation()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["EToro:MarketData:ApiKey"] = "test-api-key",
            ["EToro:MarketData:UserKey"] = "test-user-key",
            ["EToro:MarketData:QuoteCurrencyMapJson"] = "{not json"
        });

        Assert.That(
            () => _ = provider
                .GetRequiredService<IOptions<EToroMarketDataOptions>>()
                .Value,
            Throws.InvalidOperationException);
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

    private static ServiceProvider BuildQuoteProvider(
        ScriptedHttpMessageHandler handler,
        IReadOnlyDictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
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
        return services.BuildServiceProvider();
    }
}
