using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;
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
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting(ApiTestHost.ConnectionStringKey, _connectionString));
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
}
