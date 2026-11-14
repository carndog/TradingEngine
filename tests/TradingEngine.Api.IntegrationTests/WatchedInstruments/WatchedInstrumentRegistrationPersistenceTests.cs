using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;
using Testcontainers.MsSql;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Time;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
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
    public async Task PostWatchedInstrument_WhenReadinessFails_WritesNoInstrumentOrListing()
    {
        StubMarketDataProvider provider = new();
        await using WebApplicationFactory<Program> factory = CreateFactory(provider);
        HttpClient client = factory.CreateClient();

        HttpResponseMessage registration = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            new RegisterWatchedInstrumentRequest(
                "reg-reject",
                "xother",
                "gbp",
                60,
                "monitored",
                4,
                [],
                [],
                "gbp"));

        Assert.Multiple(() =>
        {
            Assert.That(
                registration.StatusCode,
                Is.EqualTo(HttpStatusCode.BadRequest));
        });

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IListingConfigurationStore listings =
                scope.ServiceProvider.GetRequiredService<IListingConfigurationStore>();
            Result<ListingConfigurationSnapshot> found = await listings.FindAsync(
                "XOTHER",
                "REG-REJECT",
                CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(found.IsFailure, Is.True);
                Assert.That(
                    found.Error,
                    Is.EqualTo(InstrumentListingErrors.NotFound));
            });

            IWatchedInstrumentStore store =
                scope.ServiceProvider.GetRequiredService<IWatchedInstrumentStore>();
            Result<WatchedInstrumentPage> page = await store.SearchAsync(
                "REG-REJECT",
                1,
                10,
                CancellationToken.None);
            Assert.That(page.Value.Items, Is.Empty);
        }
    }

    [Test]
    public async Task ListingLifecycle_WithRealPersistence_ConfirmsEnablesCollectsPausesAndResumes()
    {
        StubMarketDataProvider provider = new();
        FakeClock clock = new(Instant.FromUtc(2026, 10, 30, 13, 0, 0));
        WebApplicationFactory<Program> factory = CreateFactory(provider, clock);
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

        PriceCollectionRunResult firstRun = await RunCollectionAsync(factory);
        clock.Advance(Duration.FromSeconds(1));
        PriceCollectionRunResult secondRun = await RunCollectionAsync(factory);

        Assert.Multiple(() =>
        {
            Assert.That(firstRun.Items, Has.Count.EqualTo(1));
            Assert.That(
                firstRun.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(secondRun.Items, Has.Count.EqualTo(1));
            Assert.That(
                secondRun.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
            Assert.That(
                secondRun.Items[0].ScheduledFor,
                Is.Not.EqualTo(firstRun.Items[0].ScheduledFor));
        });

        Dictionary<DateTimeOffset, (long Observations, long ObservedAttempts)> slots =
            await ReadSlotCountsAsync(instrumentId);
        Assert.That(slots.Keys, Is.EqualTo(new DateTimeOffset[]
        {
            firstRun.Items[0].ScheduledFor.ToDateTimeUtc(),
            secondRun.Items[0].ScheduledFor.ToDateTimeUtc()
        }).AsCollection);
        foreach ((DateTimeOffset _, (long Observations, long ObservedAttempts) counts)
            in slots)
        {
            Assert.Multiple(() =>
            {
                Assert.That(counts.Observations, Is.EqualTo(1));
                Assert.That(counts.ObservedAttempts, Is.EqualTo(1));
            });
        }

        using (IServiceScope scope = factory.Services.CreateScope())
        {
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
            Assert.That(
                collecting.Collection.LastObservedAt,
                Is.Not.Null);
        });

        await PutSettingsAsync(client, instrumentId, "configured");
        clock.Advance(Duration.FromSeconds(1));
        PriceCollectionRunResult pausedRun = await RunCollectionAsync(factory);
        Assert.That(pausedRun.Items, Is.Empty);
        Assert.That(
            (await ReadSlotCountsAsync(instrumentId)).Count,
            Is.EqualTo(2));

        await PutSettingsAsync(client, instrumentId, "monitored");
        clock.Advance(Duration.FromSeconds(1));
        PriceCollectionRunResult resumedRun = await RunCollectionAsync(factory);
        Assert.Multiple(() =>
        {
            Assert.That(resumedRun.Items, Has.Count.EqualTo(1));
            Assert.That(
                resumedRun.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
        });

        Dictionary<DateTimeOffset, (long Observations, long ObservedAttempts)> resumed =
            await ReadSlotCountsAsync(instrumentId);
        Assert.That(resumed, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(
                resumed[resumedRun.Items[0].ScheduledFor.ToDateTimeUtc()].Observations,
                Is.EqualTo(1));
            Assert.That(
                resumed[resumedRun.Items[0].ScheduledFor.ToDateTimeUtc()].ObservedAttempts,
                Is.EqualTo(1));
        });

        await factory.DisposeAsync();

        FakeClock rebuiltClock = new(Instant.FromUtc(2026, 10, 30, 14, 0, 0));
        await using WebApplicationFactory<Program> rebuilt = CreateFactory(
            provider,
            rebuiltClock);
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

        PriceCollectionRunResult rebuiltRun = await RunCollectionAsync(rebuilt);
        Assert.Multiple(() =>
        {
            Assert.That(rebuiltRun.Items, Has.Count.EqualTo(1));
            Assert.That(
                rebuiltRun.Items[0].Outcome,
                Is.EqualTo(PriceCollectionOutcome.Observed));
        });
    }

    private WebApplicationFactory<Program> CreateFactory(
        IMarketDataProvider provider,
        IClock? clock = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(ApiTestHost.ConnectionStringKey, _connectionString);
                builder.UseSetting("PriceCollection:Enabled", "true");
                builder.UseSetting("PriceCollection:PollIntervalSeconds", "3600");
                builder.UseSetting("PriceCollection:MaxExecutionTimeSeconds", "10");
                builder.UseSetting("PriceCollection:ExchangeSessions:XTEST:AlwaysOpen", "true");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(provider);
                    if (clock is not null)
                    {
                        services.AddSingleton(clock);
                    }
                });
            });
    }

    private static async Task<PriceCollectionRunResult> RunCollectionAsync(
        WebApplicationFactory<Program> factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        CollectDuePricesHandler handler =
            scope.ServiceProvider.GetRequiredService<CollectDuePricesHandler>();
        return await handler.HandleAsync(new CollectDuePrices(), CancellationToken.None);
    }

    private async Task<Dictionary<DateTimeOffset, (long Observations, long ObservedAttempts)>>
        ReadSlotCountsAsync(Guid instrumentId)
    {
        Dictionary<DateTimeOffset, (long, long)> slots = [];
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT o.ScheduledFor, " +
            "COUNT_BIG(DISTINCT o.Id) AS Observations, " +
            "COUNT_BIG(DISTINCT a.Id) AS ObservedAttempts " +
            "FROM dbo.PriceObservations o " +
            "LEFT JOIN dbo.PriceCollectionAttempts a " +
            "ON a.WatchedInstrumentId = o.WatchedInstrumentId " +
            "AND a.ScheduledFor = o.ScheduledFor " +
            "AND a.Outcome = 'Observed' " +
            "WHERE o.WatchedInstrumentId = @id " +
            "GROUP BY o.ScheduledFor " +
            "ORDER BY o.ScheduledFor";
        command.Parameters.AddWithValue("@id", instrumentId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DateTimeOffset slot = new(reader.GetDateTime(0), TimeSpan.Zero);
            slots[slot] = (reader.GetInt64(1), reader.GetInt64(2));
        }

        return slots;
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

}
