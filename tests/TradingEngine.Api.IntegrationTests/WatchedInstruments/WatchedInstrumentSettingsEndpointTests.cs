using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Api.IntegrationTests.PriceObservations;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class WatchedInstrumentSettingsEndpointTests
{
    private static readonly Guid InstrumentId =
        Guid.Parse("4b6e8f2a-1c3d-4e5f-9a8b-7c6d5e4f3a2b");
    private static readonly byte[] Token = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly string TokenHeader = $"\"{Convert.ToBase64String(Token)}\"";

    private WebApplicationFactory<Program> _factory = null!;
    private StubWatchedInstrumentStore _store = null!;
    private StubListingConfigurationStore _listings = null!;
    private StubMarketDataProvider _provider = null!;
    private StubPriceObservationStore _observations = null!;
    private StubPriceObservationHistoryReader _history = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new StubWatchedInstrumentStore();
        _listings = new StubListingConfigurationStore();
        _provider = new StubMarketDataProvider();
        _observations = new StubPriceObservationStore();
        _history = new StubPriceObservationHistoryReader();
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
                builder.UseSetting(
                    "EToro:MarketData:QuoteCurrencyMap:XTEST:DEMO-2",
                    "GBP");
                builder.UseSetting("PriceCollection:ExchangeSessions:XTEST:AlwaysOpen", "true");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IWatchedInstrumentStore>(_store);
                    services.AddSingleton<IListingConfigurationStore>(_listings);
                    services.AddSingleton<IMarketDataProvider>(_provider);
                    services.AddSingleton<IPriceObservationStore>(_observations);
                    services.AddSingleton<IPriceObservationHistoryReader>(_history);
                });
            });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task GetSettings_WhenStored_ReturnsSettingsWithConcurrencyToken()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments/{InstrumentId}/settings");
        WatchedInstrumentSettingsResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentSettingsResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Id, Is.EqualTo(InstrumentId));
            Assert.That(body.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(body.Exchange, Is.EqualTo("XTEST"));
            Assert.That(body.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(body.MonitoringState, Is.EqualTo("configured"));
            Assert.That(body.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(body.ConcurrencyToken, Is.EqualTo(Convert.ToBase64String(Token)));
            Assert.That(
                response.Headers.ETag?.Tag,
                Is.EqualTo(TokenHeader));
        });
    }

    [Test]
    public async Task GetSettings_WithUnknownId_ReturnsNotFoundProblem()
    {
        _store.GetSettingsResult = WatchedInstrumentErrors.SettingsNotFound;
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments/{InstrumentId}/settings");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.settings_not_found"));
        });
    }

    [Test]
    public async Task PutSettings_WithoutIfMatch_ReturnsPreconditionRequired()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/settings",
            new UpdateWatchedInstrumentSettingsRequest("monitored", 120));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)428));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.concurrency_token_required"));
            Assert.That(_store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task PutSettings_WithMalformedIfMatch_ReturnsValidationProblem()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/settings")
        {
            Content = JsonContent.Create(
                new UpdateWatchedInstrumentSettingsRequest("monitored", 120))
        };
        request.Headers.TryAddWithoutValidation("If-Match", "not-base64!!!");

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.concurrency_token_invalid"));
            Assert.That(_store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task PutSettings_WithStaleToken_ReturnsPreconditionFailed()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/settings")
        {
            Content = JsonContent.Create(
                new UpdateWatchedInstrumentSettingsRequest("monitored", 120))
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"AAAAAAAAAAA=\"");

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.concurrent_change"));
            Assert.That(_store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task PutSettings_WithMatchingValues_ReturnsOkWithoutSaving()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await SendPutAsync(
            client,
            new UpdateWatchedInstrumentSettingsRequest("monitored", 60));
        WatchedInstrumentSettingsResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentSettingsResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.MonitoringState, Is.EqualTo("monitored"));
            Assert.That(body.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(_store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task PutSettings_WithChangedValues_SavesAndReturnsFreshToken()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await SendPutAsync(
            client,
            new UpdateWatchedInstrumentSettingsRequest("monitored", 120));
        WatchedInstrumentSettingsResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentSettingsResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.MonitoringState, Is.EqualTo("monitored"));
            Assert.That(body.SamplingIntervalSeconds, Is.EqualTo(120));
            Assert.That(body.ConcurrencyToken, Is.Not.EqualTo(Convert.ToBase64String(Token)));
            Assert.That(_store.SavedInstrument, Is.Not.Null);
            Assert.That(
                _store.SavedInstrument!.MonitoringState,
                Is.EqualTo(MonitoringState.Monitored));
            Assert.That(_store.SavedInstrument.SamplingIntervalSeconds, Is.EqualTo(120));
            Assert.That(_store.ExpectedToken, Is.EqualTo(Token));
        });
    }

    [Test]
    public async Task PutSettings_WithStoreConcurrencyConflict_ReturnsPreconditionFailed()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        _store.SaveSettingsResult = WatchedInstrumentErrors.ConcurrentChange;
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await SendPutAsync(
            client,
            new UpdateWatchedInstrumentSettingsRequest("monitored", 120));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.concurrent_change"));
        });
    }

    [Test]
    public async Task PutSettings_WithUnknownMonitoringState_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await SendPutAsync(
            client,
            new UpdateWatchedInstrumentSettingsRequest("paused", 120));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.monitoring_state_undefined"));
            Assert.That(_store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task PutSettings_WithNullBody_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/settings")
        {
            Content = JsonContent.Create<UpdateWatchedInstrumentSettingsRequest?>(null)
        };
        request.Headers.TryAddWithoutValidation("If-Match", TokenHeader);

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.request_required"));
        });
    }

    [Test]
    public async Task PutSettings_WithDisplayPreferences_SavesAndReturnsThem()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await SendPutAsync(
            client,
            new UpdateWatchedInstrumentSettingsRequest("monitored", 60, 0, "native"));
        WatchedInstrumentSettingsResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentSettingsResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.PriceDisplayDecimalPlaces, Is.EqualTo(0));
            Assert.That(body.PriceDisplayUnit, Is.EqualTo("native"));
            Assert.That(_store.SavedDisplay, Is.EqualTo(new PriceDisplayPreferences(0, PriceDisplayUnit.Native)));
            Assert.That(_store.ExpectedToken, Is.EqualTo(Token));
        });
    }

    [Test]
    public async Task PutSettings_FromClientOmittingDisplayFields_KeepsStoredPreferences()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60).Value with
        {
            Display = new PriceDisplayPreferences(0, PriceDisplayUnit.Native)
        };
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/settings")
        {
            Content = new StringContent(
                "{\"monitoringState\":\"monitored\",\"samplingIntervalSeconds\":120}",
                System.Text.Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("If-Match", TokenHeader);

        HttpResponseMessage response = await client.SendAsync(request);
        WatchedInstrumentSettingsResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentSettingsResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_store.SavedDisplay, Is.EqualTo(new PriceDisplayPreferences(0, PriceDisplayUnit.Native)));
            Assert.That(body!.PriceDisplayDecimalPlaces, Is.EqualTo(0));
            Assert.That(body.PriceDisplayUnit, Is.EqualTo("native"));
        });
    }

    [Test]
    public async Task GetSettings_WithDefaultPreferences_ReturnsTwoPlacesAndMajorUnit()
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        HttpClient client = _factory.CreateClient();

        WatchedInstrumentSettingsResponse? body = await client
            .GetFromJsonAsync<WatchedInstrumentSettingsResponse>($"/api/watched-instruments/{InstrumentId}/settings");

        Assert.Multiple(() =>
        {
            Assert.That(body!.PriceDisplayDecimalPlaces, Is.EqualTo(2));
            Assert.That(body.PriceDisplayUnit, Is.EqualTo("major"));
        });
    }

    [TestCase(9, null, "watched_instrument.price_display_decimal_places_out_of_range")]
    [TestCase(-1, null, "watched_instrument.price_display_decimal_places_out_of_range")]
    [TestCase(2, "pounds", "watched_instrument.price_display_unit_undefined")]
    public async Task PutSettings_WithInvalidDisplayPreferences_ReturnsValidationProblem(
        int decimalPlaces,
        string? unit,
        string expectedCode)
    {
        _store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await SendPutAsync(
            client,
            new UpdateWatchedInstrumentSettingsRequest("monitored", 60, decimalPlaces, unit));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ProblemCode(problem), Is.EqualTo(expectedCode));
            Assert.That(_store.SavedInstrument, Is.Null);
        });
    }

    private Task<HttpResponseMessage> SendPutAsync(
        HttpClient client,
        UpdateWatchedInstrumentSettingsRequest body)
    {
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/settings")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", TokenHeader);

        return client.SendAsync(request);
    }

    private static Result<WatchedInstrumentSettingsSnapshot> CreateSnapshot(
        MonitoringState state,
        int intervalSeconds)
    {
        WatchedInstrument instrument = WatchedInstrument.Restore(
            InstrumentId,
            "DEMO-2",
            "XTEST",
            "GBP",
            state,
            intervalSeconds,
            Instant.FromUtc(2026, 1, 2, 9, 30),
            Instant.FromUtc(2026, 1, 2, 9, 30)).Value;

        return new WatchedInstrumentSettingsSnapshot(instrument, Token);
    }

    private static string? ProblemCode(ProblemDetails? problem)
    {
        if (problem?.Extensions.TryGetValue("code", out object? code) is true
            && code is JsonElement element)
        {
            return element.GetString();
        }

        return null;
    }
}
