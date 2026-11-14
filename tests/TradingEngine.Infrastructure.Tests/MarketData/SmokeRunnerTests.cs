using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Infrastructure.MarketData;
using TradingEngine.MarketDataSmoke;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class SmokeRunnerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 30, 12, 0, 0);
    private static readonly MarketInstrumentIdentity Instrument =
        new("XTEST", "DEMO", "USD");

    private FakeClock _clock = null!;
    private ManualTimeProvider _timeProvider = null!;
    private EToroMarketDataOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _timeProvider = new ManualTimeProvider(
            delay => _clock.Advance(Duration.FromTimeSpan(delay)));
        _options = new EToroMarketDataOptions
        {
            ApiKey = "test-api-key",
            UserKey = "test-user-key",
            RequestTimeoutSeconds = 5,
            MaxExecutionTimeSeconds = 60,
            MaxRetryAttempts = 2,
            ResolutionCacheSeconds = 3600,
            ExchangeMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["XTEST"] = ["Test Exchange"]
            },
            QuoteCurrencyMap = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["XTEST"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DEMO"] = "USD"
                }
            }
        };
    }

    [Test]
    public async Task RunAsync_WhenQuoteSucceeds_PrintsQuoteAndProvenance()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(
            HttpStatusCode.OK,
            EToroPayloads.SearchBody((4242, "DEMO", 5, "Test Exchange")));
        handler.EnqueueResponse(
            HttpStatusCode.OK,
            EToroPayloads.RatesBody(
                (4242, "10.5", "10.6", "2026-10-30T14:31:12Z", "realtime")));
        IMarketDataProvider provider = CreateProvider(handler);
        StringWriter output = new();

        int exitCode = await SmokeRunner.RunAsync(
            provider,
            Instrument,
            output,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(output.ToString(), Does.Contain("SUCCESS"));
            Assert.That(output.ToString(), Does.Contain("XTEST/DEMO/USD"));
            Assert.That(output.ToString(), Does.Contain("4242"));
            Assert.That(output.ToString(), Does.Contain("10.5"));
            Assert.That(output.ToString(), Does.Contain("10.6"));
            Assert.That(output.ToString(), Does.Contain("Realtime"));
            Assert.That(output.ToString(), Does.Contain("ProviderObservedAt"));
            Assert.That(output.ToString(), Does.Contain("RetrievedAt"));
            Assert.That(output.ToString(), Does.Contain("ProviderRequestId"));
        });
    }

    [Test]
    public async Task RunAsync_WhenMappingUnresolved_PrintsErrorAndExtensions()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(
            HttpStatusCode.OK,
            EToroPayloads.SearchBody((4243, "OTHER", 5, "Test Exchange")));
        IMarketDataProvider provider = CreateProvider(handler);
        StringWriter output = new();

        int exitCode = await SmokeRunner.RunAsync(
            provider,
            Instrument,
            output,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(
                output.ToString(),
                Does.Contain("FAILURE market_data.instrument_mapping_unresolved"));
            Assert.That(output.ToString(), Does.Contain("exchange: XTEST"));
            Assert.That(output.ToString(), Does.Contain("symbol: DEMO"));
        });
    }

    [Test]
    public async Task RunAsync_WhenProviderRejectsCredentials_PrintsFailureWithoutSecrets()
    {
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueResponse(
            HttpStatusCode.Unauthorized,
            "{\"errorCode\":\"InvalidApiKey\"}");
        IMarketDataProvider provider = CreateProvider(handler);
        StringWriter output = new();

        int exitCode = await SmokeRunner.RunAsync(
            provider,
            Instrument,
            output,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output.ToString(), Does.Contain("FAILURE"));
            Assert.That(output.ToString(), Does.Contain("EToroMarketDataException"));
            Assert.That(output.ToString(), Does.Not.Contain("test-api-key"));
            Assert.That(output.ToString(), Does.Not.Contain("test-user-key"));
        });
    }

    private EToroMarketDataProvider CreateProvider(ScriptedHttpMessageHandler handler)
    {
        return new EToroMarketDataProvider(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://public-api.test/")
            },
            Options.Create(_options),
            new ProviderRateGate(_clock, _timeProvider),
            new EToroInstrumentIdCache(_clock),
            new CompositeNativeQuoteUnitMap(
                new NullListingConfigurationStore(),
                Options.Create(_options)),
            _clock,
            _timeProvider,
            NullLogger<EToroMarketDataProvider>.Instance);
    }
}
