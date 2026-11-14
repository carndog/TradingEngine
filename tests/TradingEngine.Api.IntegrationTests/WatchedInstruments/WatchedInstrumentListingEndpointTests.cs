using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Api.IntegrationTests.PriceObservations;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class WatchedInstrumentListingEndpointTests
{
    private static readonly Guid InstrumentId =
        Guid.Parse("4b6e8f2a-1c3d-4e5f-9a8b-7c6d5e4f3a2b");
    private static readonly byte[] SeededToken = [7, 7, 7, 7, 7, 7, 7, 7];
    private static readonly string SeededTokenHeader =
        $"\"{Convert.ToBase64String(SeededToken)}\"";

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
                builder.UseSetting(
                    "PriceCollection:ExchangeSessions:XTEST:AlwaysOpen",
                    "true");
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
    public async Task PutListing_WhenUnitConfirmed_SavesAndReturnsConfirmedListing()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ListingDto? body = await response.Content.ReadFromJsonAsync<ListingDto>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.ResolutionState, Is.EqualTo("resolved"));
            Assert.That(body.UnitState, Is.EqualTo("confirmed"));
            Assert.That(body.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(body.UnitSource, Is.EqualTo("administrator"));
            Assert.That(body.DeploymentConfigured, Is.True);
            Assert.That(body.ConcurrencyToken, Is.Not.Null);
            Assert.That(body.Provider, Is.EqualTo("eToro"));
            Assert.That(body.ProviderInstrumentId, Is.EqualTo(4242));

            Assert.That(_listings.SavedUpserts, Has.Count.EqualTo(1));
            ListingConfigurationUpsert saved = _listings.SavedUpserts[0];
            Assert.That(saved.Exchange, Is.EqualTo("XTEST"));
            Assert.That(saved.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(saved.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(saved.Source, Is.EqualTo(ListingUnitSource.Administrator));
            Assert.That(saved.ConfirmedBy, Is.EqualTo("unverified-local-caller"));
            Assert.That(saved.ProviderInstrumentId, Is.EqualTo(4242));
            Assert.That(_listings.ExpectedTokens, Is.EqualTo(new byte[]?[] { null }));
        });
    }

    [Test]
    public async Task PutListing_ForListingAbsentFromDeployment_ConfirmsWithoutMapping()
    {
        _store.GetSettingsResult = CreateSnapshot("NEW-1");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ListingDto? body = await response.Content.ReadFromJsonAsync<ListingDto>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.UnitState, Is.EqualTo("confirmed"));
            Assert.That(body.DeploymentConfigured, Is.False);
            Assert.That(_listings.SavedUpserts, Has.Count.EqualTo(1));
            Assert.That(_listings.SavedUpserts[0].Symbol, Is.EqualTo("NEW-1"));
        });
    }

    [Test]
    public async Task PutListing_WhenProviderSuppliesUnit_PersistsProviderMetadataSource()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        _provider.ListingResponder = (query, _) => Task.FromResult<Result<MarketListing>>(
            new MarketListing(
                "eToro",
                4242,
                query.Symbol,
                "Test Exchange",
                5,
                "Demo Two",
                "GBP"));
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest(null));
        ListingDto? body = await response.Content.ReadFromJsonAsync<ListingDto>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.UnitState, Is.EqualTo("confirmed"));
            Assert.That(body.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(body.UnitSource, Is.EqualTo("provider-metadata"));
            Assert.That(_listings.SavedUpserts, Has.Count.EqualTo(1));
            Assert.That(
                _listings.SavedUpserts[0].Source,
                Is.EqualTo(ListingUnitSource.ProviderMetadata));
        });
    }

    [Test]
    public async Task PutListing_WhenUnitMissing_ReturnsValidationProblem()
    {
        _store.GetSettingsResult = CreateSnapshot("NEW-1");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest(null));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("instrument_listing.native_unit_required"));
            Assert.That(_listings.SavedUpserts, Is.Empty);
        });
    }

    [Test]
    public async Task PutListing_WhenUnitConflictsWithInstrument_ReturnsConflict()
    {
        _store.GetSettingsResult = CreateSnapshot("NEW-1");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("USD"));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("market_data.quote_currency_conflict"));
            Assert.That(_listings.SavedUpserts, Is.Empty);
        });
    }

    [Test]
    public async Task PutListing_WhenProviderUnitConflictsWithRequest_ReturnsConflict()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        _provider.ListingResponder = (query, _) => Task.FromResult<Result<MarketListing>>(
            new MarketListing(
                "eToro",
                4242,
                query.Symbol,
                "Test Exchange",
                5,
                "Demo Two",
                "USD"));
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("market_data.quote_currency_conflict"));
            Assert.That(_listings.SavedUpserts, Is.Empty);
        });
    }

    [Test]
    public async Task PutListing_WhenPersistedConflictsWithDeployment_ReturnsSourceConflict()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        _listings.Seed(CreateListing("DEMO-2", "USD"));
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("market_data.quote_currency_source_conflict"));
            Assert.That(_listings.SavedUpserts, Is.Empty);
        });
    }

    [Test]
    public async Task PutListing_WhenInstrumentMissing_ReturnsNotFound()
    {
        _store.GetSettingsResult = WatchedInstrumentErrors.SettingsNotFound;
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.settings_not_found"));
        });
    }

    [TestCase(
        "unresolved",
        HttpStatusCode.NotFound,
        "market_data.instrument_mapping_unresolved")]
    [TestCase(
        "ambiguous",
        HttpStatusCode.Conflict,
        "market_data.instrument_mapping_ambiguous")]
    [TestCase(
        "unavailable",
        HttpStatusCode.PreconditionFailed,
        "market_data.provider_unavailable")]
    public async Task PutListing_WhenProviderCannotResolve_ReturnsProblem(
        string mode,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        _provider.ListingResponder = (query, _) => Task.FromResult<Result<MarketListing>>(
            mode switch
            {
                "unresolved" => MarketDataErrors.InstrumentMappingUnresolved,
                "ambiguous" => MarketDataErrors.InstrumentMappingAmbiguous,
                _ => MarketDataErrors.ProviderUnavailable
            });
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(ProblemCode(problem), Is.EqualTo(expectedCode));
            Assert.That(_listings.SavedUpserts, Is.Empty);
        });
    }

    [Test]
    public async Task PutListing_WithMalformedIfMatch_ReturnsValidationProblem()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/listing")
        {
            Content = JsonContent.Create(new ConfirmListingUnitRequest("GBP"))
        };
        request.Headers.TryAddWithoutValidation("If-Match", "not-base64!!!");

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("instrument_listing.concurrency_token_invalid"));
            Assert.That(_listings.SavedUpserts, Is.Empty);
        });
    }

    [Test]
    public async Task PutListing_WithoutToken_WhenConfigurationExists_ReturnsPreconditionRequired()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        _listings.Seed(CreateListing("DEMO-2", "GBP"));
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{InstrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)428));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("instrument_listing.concurrency_token_required"));
        });
    }

    [Test]
    public async Task PutListing_WithStaleToken_ReturnsPreconditionFailed()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        _listings.Seed(CreateListing("DEMO-2", "GBP"));
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/listing")
        {
            Content = JsonContent.Create(new ConfirmListingUnitRequest("GBP"))
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"AAAAAAAAAAA=\"");

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("instrument_listing.concurrent_change"));
        });
    }

    [Test]
    public async Task PutListing_WithMatchingToken_UpdatesAndReturnsFreshToken()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        _listings.Seed(CreateListing("DEMO-2", "GBP"));
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/listing")
        {
            Content = JsonContent.Create(new ConfirmListingUnitRequest("GBP"))
        };
        request.Headers.TryAddWithoutValidation("If-Match", SeededTokenHeader);

        HttpResponseMessage response = await client.SendAsync(request);
        ListingDto? body = await response.Content.ReadFromJsonAsync<ListingDto>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.UnitState, Is.EqualTo("confirmed"));
            Assert.That(_listings.SavedUpserts, Has.Count.EqualTo(1));
            Assert.That(_listings.ExpectedTokens, Has.Count.EqualTo(1));
            Assert.That(_listings.ExpectedTokens[0], Is.EqualTo(SeededToken));
        });
    }

    [Test]
    public async Task PutListing_WithNullBody_ReturnsValidationProblem()
    {
        _store.GetSettingsResult = CreateSnapshot("DEMO-2");
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{InstrumentId}/listing")
        {
            Content = JsonContent.Create<ConfirmListingUnitRequest?>(null)
        };

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

    private static Result<WatchedInstrumentSettingsSnapshot> CreateSnapshot(string symbol)
    {
        WatchedInstrument instrument = WatchedInstrument.Restore(
            InstrumentId,
            symbol,
            "XTEST",
            "GBP",
            MonitoringState.Configured,
            60,
            Instant.FromUtc(2026, 1, 2, 9, 30),
            Instant.FromUtc(2026, 1, 2, 9, 30)).Value;

        return new WatchedInstrumentSettingsSnapshot(instrument, [1, 2, 3, 4]);
    }

    private static ListingConfigurationSnapshot CreateListing(string symbol, string unit)
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        InstrumentListing listing = InstrumentListing.Restore(
            "XTEST",
            symbol,
            "eToro",
            4242,
            symbol,
            "Test Exchange",
            "Demo Two",
            unit,
            ListingUnitSource.Administrator,
            "tester",
            now,
            now).Value;

        return new ListingConfigurationSnapshot(listing, SeededToken);
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
