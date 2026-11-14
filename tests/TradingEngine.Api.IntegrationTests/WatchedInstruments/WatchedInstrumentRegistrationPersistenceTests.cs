using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class WatchedInstrumentRegistrationPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private MsSqlContainer _container = null!;
    private string _connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(_connectionString)
                .Options;
        await using TradingEngineDbContext context = new(options);
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task PostWatchedInstrument_WithRealInfrastructure_PersistsConfiguration()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(
            new StubMarketDataProvider());
        HttpClient client = factory.CreateClient();
        RegisterWatchedInstrumentRequest request = new(
            "infra-reg",
            "xtest",
            "gbp",
            60,
            "configured",
            4,
            [new ChartZoneDto(
                "support-a",
                95m,
                100m,
                105m,
                [
                    new ChartConditionDto("buy-zone", "publish-signal"),
                    new ChartConditionDto("support-loss", "publish-signal")
                ])],
            []);

        HttpResponseMessage registration = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        string registrationBody = await registration.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"POST status={(int)registration.StatusCode} body={registrationBody}");
        WatchedInstrumentResponse? registered = registration.IsSuccessStatusCode
            ? JsonSerializer.Deserialize<WatchedInstrumentResponse>(registrationBody, JsonOptions)
            : null;

        Assert.Multiple(() =>
        {
            Assert.That(registration.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(registered, Is.Not.Null);
            Assert.That(registered!.Id, Is.Not.EqualTo(Guid.Empty));
        });

        HttpResponseMessage readBack = await client.GetAsync(
            $"/api/watched-instruments/{registered!.Id}");
        WatchedInstrumentResponse? persisted = await readBack.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(readBack.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Symbol, Is.EqualTo("INFRA-REG"));
            Assert.That(persisted.Exchange, Is.EqualTo("XTEST"));
            Assert.That(persisted.MonitoringState, Is.EqualTo("configured"));
            Assert.That(persisted.SupportZones, Has.Count.EqualTo(1));
        });

        HttpResponseMessage timelineRead = await client.GetAsync(
            $"/api/watched-instruments/{registered.Id}/monitoring-rule");
        MonitoringRuleTimelineResponse? timeline = await timelineRead.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(timelineRead.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(timeline, Is.Not.Null);
            Assert.That(timeline!.Revisions, Has.Count.EqualTo(1));
            Assert.That(timeline.Revisions[0].Kind, Is.EqualTo("committed"));
            Assert.That(timeline.Revisions[0].CreatedBy, Is.EqualTo("watched-instrument-registration"));
        });
    }

    [Test]
    public async Task ListingLifecycle_WithRealPersistence_ConfirmsEnablesCollectsPausesAndResumes()
    {
        StubMarketDataProvider provider = new();
        WebApplicationFactory<Program> factory = CreateFactory(provider);
        HttpClient client = factory.CreateClient();

        HttpResponseMessage registration = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            new RegisterWatchedInstrumentRequest(
                "listing-e2e",
                "xtest",
                "gbp",
                1,
                "configured",
                4,
                [new ChartZoneDto(
                    "support-a",
                    95m,
                    100m,
                    105m,
                    [
                        new ChartConditionDto("buy-zone", "publish-signal"),
                        new ChartConditionDto("support-loss", "publish-signal")
                    ])],
                []));
        WatchedInstrumentResponse? registered = await registration.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(registration.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(registered, Is.Not.Null);
            Assert.That(registered!.Listing, Is.Not.Null);
            Assert.That(registered.Listing!.ResolutionState, Is.EqualTo("resolved"));
            Assert.That(registered.Listing.UnitState, Is.EqualTo("unconfirmed"));
        });
        Guid instrumentId = registered!.Id;

        HttpResponseMessage confirm = await client.PutAsJsonAsync(
            $"/api/watched-instruments/{instrumentId}/listing",
            new ConfirmListingUnitRequest("GBP"));
        ListingDto? listing = await confirm.Content.ReadFromJsonAsync<ListingDto>();
        Assert.Multiple(() =>
        {
            Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(listing, Is.Not.Null);
            Assert.That(listing!.UnitState, Is.EqualTo("confirmed"));
            Assert.That(listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(listing.UnitSource, Is.EqualTo("administrator"));
        });

        await PutSettingsAsync(client, instrumentId, "monitored");

        await WaitForObservationsAsync(client, instrumentId, 2, TimeSpan.FromSeconds(45));

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IPriceObservationStore observations =
                scope.ServiceProvider.GetRequiredService<IPriceObservationStore>();
            IReadOnlyDictionary<Guid, PriceCollectionAttempt> attempts =
                await observations.GetLatestAttemptsAsync([instrumentId], CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(attempts, Contains.Key(instrumentId));
                Assert.That(
                    attempts[instrumentId].Outcome,
                    Is.EqualTo(PriceCollectionOutcome.Observed));
            });

            IListingConfigurationStore listings =
                scope.ServiceProvider.GetRequiredService<IListingConfigurationStore>();
            Assert.That(
                (await listings.FindAsync("XTEST", "LISTING-E2E", CancellationToken.None))
                    .IsSuccess,
                Is.True);
        }

        WatchedInstrumentSettingsResponse? collecting = await GetSettingsAsync(
            client,
            instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(collecting, Is.Not.Null);
            Assert.That(collecting!.Collection, Is.Not.Null);
            Assert.That(collecting.Collection!.Status, Is.EqualTo("collecting"));
            Assert.That(collecting.Collection.LastObservedAt, Is.Not.Null);
        });

        await PutSettingsAsync(client, instrumentId, "configured");
        int pausedCount = await ObservationCountAsync(client, instrumentId);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.That(
            await ObservationCountAsync(client, instrumentId),
            Is.EqualTo(pausedCount));

        await PutSettingsAsync(client, instrumentId, "monitored");
        await WaitForObservationsAsync(
            client,
            instrumentId,
            pausedCount + 1,
            TimeSpan.FromSeconds(45));

        await factory.DisposeAsync();

        await using WebApplicationFactory<Program> rebuilt = CreateFactory(provider);
        HttpClient rebuiltClient = rebuilt.CreateClient();
        WatchedInstrumentSettingsResponse? afterRebuild = await GetSettingsAsync(
            rebuiltClient,
            instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(afterRebuild, Is.Not.Null);
            Assert.That(afterRebuild!.Listing, Is.Not.Null);
            Assert.That(afterRebuild.Listing!.UnitState, Is.EqualTo("confirmed"));
            Assert.That(afterRebuild.Listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
        });

        using (IServiceScope scope = rebuilt.Services.CreateScope())
        {
            INativeQuoteUnitMap quoteUnits =
                scope.ServiceProvider.GetRequiredService<INativeQuoteUnitMap>();
            Result<NativeQuoteUnitResolution> resolution = await quoteUnits.ResolveAsync(
                "XTEST",
                "LISTING-E2E",
                CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(resolution.IsSuccess, Is.True);
                Assert.That(resolution.Value.NativeQuoteCurrency, Is.EqualTo("GBP"));
                Assert.That(resolution.Value.Persisted, Is.Not.Null);
                Assert.That(resolution.Value.SourceConflict, Is.False);
            });
        }
    }

    private WebApplicationFactory<Program> CreateFactory(IMarketDataProvider provider)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(ApiTestHost.ConnectionStringKey, _connectionString);
                builder.UseSetting("PriceCollection:Enabled", "true");
                builder.UseSetting("PriceCollection:PollIntervalSeconds", "1");
                builder.UseSetting("PriceCollection:MaxExecutionTimeSeconds", "10");
                builder.UseSetting("PriceCollection:ExchangeSessions:XTEST:AlwaysOpen", "true");
                builder.ConfigureServices(services =>
                    services.AddSingleton(provider));
            });
    }

    private static async Task<WatchedInstrumentSettingsResponse?> GetSettingsAsync(
        HttpClient client,
        Guid instrumentId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments/{instrumentId}/settings");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return await response.Content.ReadFromJsonAsync<WatchedInstrumentSettingsResponse>();
    }

    private static async Task PutSettingsAsync(
        HttpClient client,
        Guid instrumentId,
        string monitoringState)
    {
        WatchedInstrumentSettingsResponse? settings = await GetSettingsAsync(
            client,
            instrumentId);
        Assert.That(settings, Is.Not.Null);

        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/watched-instruments/{instrumentId}/settings")
        {
            Content = JsonContent.Create(new UpdateWatchedInstrumentSettingsRequest(
                monitoringState,
                settings!.SamplingIntervalSeconds,
                settings.PriceDisplayDecimalPlaces,
                settings.PriceDisplayUnit))
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{settings.ConcurrencyToken}\"");

        HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine(
            $"PUT settings state={monitoringState} status={(int)response.StatusCode} body={body}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<int> ObservationCountAsync(HttpClient client, Guid instrumentId)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments/{instrumentId}/price-observations?pageSize=100");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        PriceObservationPageResponse? page = await response.Content
            .ReadFromJsonAsync<PriceObservationPageResponse>();
        return page?.Items.Count ?? 0;
    }

    private static async Task WaitForObservationsAsync(
        HttpClient client,
        Guid instrumentId,
        int minimum,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        int count = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            count = await ObservationCountAsync(client, instrumentId);
            if (count >= minimum)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400));
        }

        Assert.That(count, Is.GreaterThanOrEqualTo(minimum));
    }
}
