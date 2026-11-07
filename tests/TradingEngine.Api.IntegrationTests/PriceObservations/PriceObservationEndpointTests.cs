using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceObservations;

namespace TradingEngine.Api.IntegrationTests.PriceObservations;

[TestFixture]
public sealed class PriceObservationEndpointTests
{
    private static readonly Guid InstrumentId = Guid.Parse("e1030000-0000-0000-0000-0000000000a1");
    private static readonly Instant Slot = Instant.FromUtc(2026, 10, 30, 13, 45);

    private StubPriceObservationHistoryReader _reader = null!;
    private CountingMarketDataProvider _provider = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _reader = new StubPriceObservationHistoryReader();
        _provider = new CountingMarketDataProvider();
        _factory = CreateFactory(new Dictionary<string, string?>());
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task Get_StoredHistory_ReturnsFullPrecisionNullableSidesAndUtcTimes()
    {
        Instant scheduled = Slot.PlusTicks(1234567);
        PriceObservationRecord newest = new(
            scheduled,
            Slot.Plus(Duration.FromSeconds(30)),
            Slot.Plus(Duration.FromSeconds(28)).PlusTicks(1),
            123.456789012345678901m,
            null,
            "GBX",
            "synthetic",
            MarketQuoteTimeliness.Delayed);
        PriceObservationRecord older = new(
            Slot.Minus(Duration.FromMinutes(1)),
            Slot,
            null,
            null,
            0.000000000000000001m,
            "GBX",
            "synthetic",
            MarketQuoteTimeliness.Unknown);
        _reader.Result = new PriceObservationHistory(
            InstrumentId, newest, [newest, older], 25, null, older.ScheduledFor);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Path(string.Empty));
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        JsonElement first = root.GetProperty("items")[0];
        JsonElement second = root.GetProperty("items")[1];

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("123.456789012345678901"));
            Assert.That(body, Does.Contain("0.000000000000000001"));
            Assert.That(first.GetProperty("bid").GetDecimal(), Is.EqualTo(123.456789012345678901m));
            Assert.That(first.GetProperty("ask").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(second.GetProperty("bid").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(first.GetProperty("scheduledFor").GetString(), Is.EqualTo("2026-10-30T13:45:00.1234567+00:00"));
            Assert.That(first.GetProperty("providerObservedAt").GetString(), Is.EqualTo("2026-10-30T13:45:28.0000001+00:00"));
            Assert.That(second.GetProperty("providerObservedAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(first.GetProperty("quoteCurrency").GetString(), Is.EqualTo("GBX"));
            Assert.That(first.GetProperty("timeliness").GetString(), Is.EqualTo("delayed"));
            Assert.That(second.GetProperty("timeliness").GetString(), Is.EqualTo("unknown"));
            Assert.That(root.GetProperty("latest").GetProperty("ask").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("nextBefore").GetString(), Is.EqualTo("2026-10-30T13:44:00+00:00"));
            Assert.That(root.GetProperty("pageSize").GetInt32(), Is.EqualTo(25));
            Assert.That(_reader.LastPageSize, Is.EqualTo(25));
            Assert.That(_reader.LastBefore, Is.Null);
            Assert.That(_reader.LastTokenCanBeCancelled, Is.True);
        });
    }

    [Test]
    public async Task Get_EmptyHistory_ReturnsOkWithNoLatest()
    {
        _reader.Result = new PriceObservationHistory(InstrumentId, null, [], 25, null, null);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Path(string.Empty));
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(document.RootElement.GetProperty("latest").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(document.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Get_UnknownInstrument_ReturnsNotFoundProblem()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Path(string.Empty));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(body, Does.Contain("price_observation.instrument_not_found"));
        });
    }

    [Test]
    public async Task Get_FullPrecisionCursorAndPageSize_PassesThemToReader()
    {
        _reader.Result = new PriceObservationHistory(InstrumentId, null, [], 100, null, null);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            Path("?before=2026-10-30T13:45:00.1234567Z&pageSize=100"));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_reader.LastBefore, Is.EqualTo(Slot.PlusTicks(1234567)));
            Assert.That(_reader.LastPageSize, Is.EqualTo(100));
        });
    }

    [Test]
    public async Task Get_EmittedNextBeforeCursorReusedUnchanged_ReadsNextPage()
    {
        Instant boundary = Slot.PlusTicks(1234567);
        PriceObservationRecord row = new(
            boundary,
            Slot.Plus(Duration.FromSeconds(30)),
            null,
            1.5m,
            1.6m,
            "GBX",
            "synthetic",
            MarketQuoteTimeliness.Realtime);
        _reader.Result = new PriceObservationHistory(InstrumentId, row, [row], 1, null, boundary);
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage first = await client.GetAsync(Path("?pageSize=1"));
        string firstBody = await first.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(firstBody);
        string nextBefore = document.RootElement.GetProperty("nextBefore").GetString()!;

        HttpResponseMessage second = await client.GetAsync(
            Path($"?pageSize=1&before={Uri.EscapeDataString(nextBefore)}"));

        Assert.Multiple(() =>
        {
            Assert.That(nextBefore, Is.EqualTo("2026-10-30T13:45:00.1234567+00:00"));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_reader.LastBefore, Is.EqualTo(boundary));
        });
    }

    [TestCase("?pageSize=0", "price_observation.page_size_out_of_range")]
    [TestCase("?pageSize=101", "price_observation.page_size_out_of_range")]
    [TestCase("?pageSize=abc", "price_observation.page_size_out_of_range")]
    [TestCase("?pageSize=-5", "price_observation.page_size_out_of_range")]
    [TestCase("?before=not-a-time", "price_observation.cursor_invalid")]
    [TestCase("?before=2026-10-30T13:45:00+01:00", "price_observation.cursor_invalid")]
    [TestCase("?before=2026-10-30T13:45:00.123456789Z", "price_observation.cursor_invalid")]
    [TestCase("?before=2026-10-30T13:45:00.123456789%2B00:00", "price_observation.cursor_invalid")]
    [TestCase("?before=", "price_observation.cursor_invalid")]
    public async Task Get_InvalidPagingInput_ReturnsValidationProblemWithoutReading(string query, string code)
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Path(query));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain(code));
            Assert.That(_reader.CallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Get_InvalidInstrumentId_ReturnsNotFoundWithoutReading()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watched-instruments/not-a-guid/price-observations");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(_reader.CallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Get_RepeatedRefresh_DoesNotCallMarketDataProvider()
    {
        _reader.Result = new PriceObservationHistory(InstrumentId, null, [], 25, null, null);
        HttpClient client = _factory.CreateClient();

        await client.GetAsync(Path(string.Empty));
        await client.GetAsync(Path(string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(_reader.CallCount, Is.EqualTo(2));
            Assert.That(_provider.CallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Get_TrustedHeadersWithoutPrincipal_ReturnsUnauthorizedWithoutReading()
    {
        using WebApplicationFactory<Program> trusted = CreateFactory(new Dictionary<string, string?>
        {
            ["Authentication:EasyAuth:TrustPlatformHeaders"] = "true"
        });
        HttpClient client = trusted.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Path(string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(_reader.CallCount, Is.EqualTo(0));
        });
    }

    private WebApplicationFactory<Program> CreateFactory(IDictionary<string, string?> settings)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IPriceObservationHistoryReader>(_reader);
                    services.AddSingleton<IMarketDataProvider>(_provider);
                });
            });
    }

    private static string Path(string query)
    {
        return $"/api/watched-instruments/{InstrumentId}/price-observations{query}";
    }
}
