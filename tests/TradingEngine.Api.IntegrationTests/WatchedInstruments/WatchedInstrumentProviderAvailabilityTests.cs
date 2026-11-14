using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Application.Ports;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class WatchedInstrumentProviderAvailabilityTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private StubWatchedInstrumentStore _store = null!;
    private StubListingConfigurationStore _listings = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new StubWatchedInstrumentStore();
        _listings = new StubListingConfigurationStore();
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
                builder.UseSetting("PriceCollection:ExchangeSessions:XTEST:AlwaysOpen", "true");
                builder.UseSetting("EToro:MarketData:ApiKey", string.Empty);
                builder.UseSetting("EToro:MarketData:UserKey", string.Empty);
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IWatchedInstrumentStore>(_store);
                    services.AddSingleton<IListingConfigurationStore>(_listings);
                });
            });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task PostWatchedInstrument_WhenCredentialsMissingAndConfigured_ReturnsCreatedWithUnavailableResolution()
    {
        HttpClient client = _factory.CreateClient();
        RegisterWatchedInstrumentRequest request = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            "configured",
            4,
            [],
            []);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        WatchedInstrumentResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.MonitoringState, Is.EqualTo("configured"));
            Assert.That(body.Listing, Is.Not.Null);
            Assert.That(
                body.Listing!.ResolutionState,
                Is.EqualTo("unavailable"));
            Assert.That(
                body.Listing.ResolutionDetailCode,
                Is.EqualTo("market_data.provider_credentials_unconfigured"));
            Assert.That(body.Listing.UnitState, Is.EqualTo("unconfirmed"));
            Assert.That(_store.AddedRegistration, Is.Not.Null);
        });
    }

    [Test]
    public async Task PostWatchedInstrument_WhenCredentialsMissingAndMonitored_ReturnsPreconditionFailed()
    {
        HttpClient client = _factory.CreateClient();
        RegisterWatchedInstrumentRequest request = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            "monitored",
            4,
            [],
            [],
            "gbp");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(
                response.StatusCode,
                Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(problem, Is.Not.Null);
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("market_data.provider_credentials_unconfigured"));
            Assert.That(_store.AddedRegistration, Is.Null);
        });
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
