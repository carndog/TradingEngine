using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class InstrumentVerificationEndpointTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 12, 0);
    private static readonly VerifyInstrumentRequest Request = new("DEMO-2", "XTEST", "USD");
    private WebApplicationFactory<Program> _factory = null!;
    private StubWatchedInstrumentStore _store = null!;
    private StubListingConfigurationStore _listings = null!;
    private StubPriceObservationStore _observations = null!;
    private StubMarketDataProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new StubWatchedInstrumentStore();
        _listings = new StubListingConfigurationStore();
        _observations = new StubPriceObservationStore();
        _provider = new StubMarketDataProvider
        {
            QuoteResponder = (identity, _) => Task.FromResult<Result<MarketQuote>>(
                new MarketQuote(identity, "eToro", 4242, 12m, null, Now - Duration.FromMinutes(15),
                    Now, MarketQuoteTimeliness.Delayed, "synthetic-internal-correlation"))
        };
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSyntheticConnectionString();
            builder.UseSetting("EToro:MarketData:QuoteCurrencyMap:XTEST:DEMO-2", "USD");
            builder.UseSetting("PriceCollection:ExchangeSessions:XTEST:AlwaysOpen", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:EasyAuth:TrustPlatformHeaders"] = "true"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IClock>(new FakeClock(Now));
                services.AddSingleton<IWatchedInstrumentStore>(_store);
                services.AddSingleton<IListingConfigurationStore>(_listings);
                services.AddSingleton<IPriceObservationStore>(_observations);
                services.AddSingleton<IMarketDataProvider>(_provider);
            });
        });
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task VerifyAsync_AuthenticatedUnregisteredListing_ReturnsSanitizedQuoteWithoutWrites()
    {
        using HttpClient client = Client();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/watched-instruments/verify", Request);
        InstrumentVerificationResponse? body = await response.Content.ReadFromJsonAsync<InstrumentVerificationResponse>();
        string json = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl!.NoStore, Is.True);
        Assert.That(body!.ResolutionState, Is.EqualTo("resolved"));
        Assert.That(body.ProviderSymbol, Is.EqualTo("DEMO-2"));
        Assert.That(body.ProviderExchangeName, Is.EqualTo("Test Exchange"));
        Assert.That(body.ReadinessDetailCode, Is.Null);
        Assert.That(body.ReadyForMonitoring, Is.True);
        Assert.That(body.Quote!.Bid, Is.EqualTo(12m));
        Assert.That(body.Quote.Ask, Is.Null);
        Assert.That(body.Quote.Timeliness, Is.EqualTo("delayed"));
        Assert.That(body.Quote.ProviderObservedAt, Is.EqualTo((Now - Duration.FromMinutes(15)).ToDateTimeOffset()));
        Assert.That(json, Does.Not.Contain("synthetic-internal-correlation"));
        AssertNoWrites();
    }

    [Test]
    public async Task VerifyAsync_NoTrustedPrincipal_RejectsBeforeProviderCall()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/watched-instruments/verify", Request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_provider.ListingQueries, Is.Empty);
        AssertNoWrites();
    }

    [Test]
    public async Task VerifyAsync_UnknownListing_ReturnsNotFoundStateWithoutWrites()
    {
        _provider.ListingResponder = (_, _) => Task.FromResult<Result<MarketListing>>(MarketDataErrors.InstrumentMappingUnresolved);
        using HttpClient client = Client();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/watched-instruments/verify", Request);
        InstrumentVerificationResponse? body = await response.Content.ReadFromJsonAsync<InstrumentVerificationResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.ResolutionState, Is.EqualTo("not-found"));
        Assert.That(body.ReadyForMonitoring, Is.False);
        Assert.That(_provider.QuoteRequests, Is.Empty);
        AssertNoWrites();
    }

    [Test]
    public async Task VerifyAsync_MissingUnit_RecognisesListingAndAllowsConfiguredRegistration()
    {
        using HttpClient client = Client();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/watched-instruments/verify",
            new VerifyInstrumentRequest("NEW-1", "XTEST", "USD", "USD"));
        InstrumentVerificationResponse? body = await response.Content.ReadFromJsonAsync<InstrumentVerificationResponse>();

        Assert.That(body!.ResolutionState, Is.EqualTo("resolved"));
        Assert.That(body.ReadinessDetailCode, Is.EqualTo("market_data.quote_currency_unconfigured"));
        AssertNoWrites();

        HttpResponseMessage registered = await client.PostAsJsonAsync("/api/watched-instruments",
            new RegisterWatchedInstrumentRequest("NEW-1", "XTEST", "USD", 60, "configured", 4, [], []));

        Assert.That(registered.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(_store.AddedRegistration, Is.Not.Null);
    }

    [Test]
    public async Task VerifyAsync_InvalidInput_ReturnsValidationFailureWithoutProviderCall()
    {
        using HttpClient client = Client();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/watched-instruments/verify", Request with { Symbol = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(_provider.ListingQueries, Is.Empty);
        AssertNoWrites();
    }

    private HttpClient Client()
    {
        HttpClient client = _factory.CreateClient();
        string principal = """
            {"auth_typ":"aad","claims":[{"typ":"http://schemas.microsoft.com/identity/claims/objectidentifier","val":"11111111-2222-3333-4444-555555555555"}],"name_typ":"name","role_typ":"roles"}
            """;
        client.DefaultRequestHeaders.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(Encoding.UTF8.GetBytes(principal)));
        return client;
    }

    private void AssertNoWrites()
    {
        Assert.That(_store.AddedRegistration, Is.Null);
        Assert.That(_listings.SavedUpserts, Is.Empty);
        Assert.That(_observations.Observations, Is.Empty);
        Assert.That(_observations.Attempts, Is.Empty);
    }
}
