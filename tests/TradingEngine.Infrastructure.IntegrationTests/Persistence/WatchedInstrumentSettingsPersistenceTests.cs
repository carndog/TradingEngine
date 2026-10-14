using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class WatchedInstrumentSettingsPersistenceTests
{
    private MsSqlContainer _container = null!;
    private string _connectionString = null!;
    private ChartAnalysisDefinitionXmlSerializer _serializer = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        _serializer = new ChartAnalysisDefinitionXmlSerializer();

        await using TradingEngineDbContext context = CreateContext("Settings");
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsRowVersionConcurrencyToken()
    {
        Guid id = await AddInstrumentAsync();

        Result<WatchedInstrumentSettingsSnapshot> snapshot;
        await using (TradingEngineDbContext context = CreateContext("Settings"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            snapshot = await store.GetSettingsAsync(id, CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.IsSuccess, Is.True);
            Assert.That(snapshot.Value.ConcurrencyToken, Is.Not.Empty);
            Assert.That(snapshot.Value.Instrument.Id, Is.EqualTo(id));
            Assert.That(
                snapshot.Value.Instrument.MonitoringState,
                Is.EqualTo(MonitoringState.Configured));
        });
    }

    [Test]
    public async Task SaveSettingsAsync_WithExpectedToken_PersistsAndRotatesToken()
    {
        Guid id = await AddInstrumentAsync();
        byte[] firstToken;
        WatchedInstrument instrument;

        await using (TradingEngineDbContext context = CreateContext("Settings"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentSettingsSnapshot> snapshot =
                await store.GetSettingsAsync(id, CancellationToken.None);
            firstToken = snapshot.Value.ConcurrencyToken;
            instrument = snapshot.Value.Instrument;
            instrument.StartMonitoring(120, Instant.FromUtc(2026, 10, 5, 9, 30));

            Result<WatchedInstrumentSettingsSnapshot> saved = await store.SaveSettingsAsync(
                instrument,
                firstToken,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(saved.IsSuccess, Is.True);
                Assert.That(saved.Value.ConcurrencyToken, Is.Not.EqualTo(firstToken));
            });
        }

        await using (TradingEngineDbContext context = CreateContext("Settings"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentSettingsSnapshot> reloaded =
                await store.GetSettingsAsync(id, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.IsSuccess, Is.True);
                Assert.That(
                    reloaded.Value.Instrument.MonitoringState,
                    Is.EqualTo(MonitoringState.Monitored));
                Assert.That(reloaded.Value.Instrument.SamplingIntervalSeconds, Is.EqualTo(120));
            });
        }
    }

    [Test]
    public async Task SaveSettingsAsync_WithStaleToken_ReturnsConcurrentChange()
    {
        Guid id = await AddInstrumentAsync();
        byte[] firstToken;

        await using (TradingEngineDbContext context = CreateContext("Settings"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentSettingsSnapshot> snapshot =
                await store.GetSettingsAsync(id, CancellationToken.None);
            firstToken = snapshot.Value.ConcurrencyToken;
            WatchedInstrument winner = snapshot.Value.Instrument;
            winner.StartMonitoring(60, Instant.FromUtc(2026, 10, 5, 9, 30));
            Result<WatchedInstrumentSettingsSnapshot> saved = await store.SaveSettingsAsync(
                winner,
                firstToken,
                CancellationToken.None);
            Assert.That(saved.IsSuccess, Is.True);
        }

        Result<WatchedInstrumentSettingsSnapshot> stale;
        WatchedInstrument? loser = null;
        await using (TradingEngineDbContext context = CreateContext("Settings"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentSettingsSnapshot> snapshot =
                await store.GetSettingsAsync(id, CancellationToken.None);
            loser = snapshot.Value.Instrument;
            loser.ChangeSamplingInterval(30, Instant.FromUtc(2026, 10, 5, 10, 0));

            stale = await store.SaveSettingsAsync(
                loser,
                firstToken,
                CancellationToken.None);
        }

        Assert.That(stale.IsFailure, Is.True);
        Assert.That(stale.Error, Is.EqualTo(WatchedInstrumentErrors.ConcurrentChange));

        await using (TradingEngineDbContext context = CreateContext("Settings"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentSettingsSnapshot> reloaded =
                await store.GetSettingsAsync(id, CancellationToken.None);
            Assert.That(reloaded.Value.Instrument.SamplingIntervalSeconds, Is.EqualTo(60));
        }
    }

    [Test]
    public async Task GetSettingsAsync_WithUnknownId_ReturnsNotFound()
    {
        Result<WatchedInstrumentSettingsSnapshot> snapshot;
        await using (TradingEngineDbContext context = CreateContext("Settings"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            snapshot = await store.GetSettingsAsync(
                Guid.Parse("6c77c1f7-8b6c-4a0d-b6d7-1a2b3c4d5e6f"),
                CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.IsFailure, Is.True);
            Assert.That(snapshot.Error, Is.EqualTo(WatchedInstrumentErrors.SettingsNotFound));
        });
    }

    private async Task<Guid> AddInstrumentAsync()
    {
        await using TradingEngineDbContext context = CreateContext("Settings");
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        WatchedInstrumentFields fields = WatchedInstrument
            .Validate($"SET-{Guid.NewGuid():N}".ToUpperInvariant()[..20], "XTEST", "USD", 60)
            .Value;
        Result<Guid> added = await store.AddAsync(
            new WatchedInstrumentRegistration(
                fields,
                MonitoringState.Configured,
                Instant.FromUtc(2026, 1, 2, 9, 30),
                CreateDefinition()),
            CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);

        return added.Value;
    }

    private TradingEngineDbContext CreateContext(string database)
    {
        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(ConnectionStringFor(database))
                .Options;

        return new TradingEngineDbContext(options);
    }

    private string ConnectionStringFor(string database)
    {
        return $"{_connectionString};Database={database}";
    }

    private static ChartAnalysisDefinition CreateDefinition()
    {
        return ChartAnalysisDefinition.Create(
            4,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    95m,
                    100m,
                    105m,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value
                    ]).Value
            ],
            []).Value;
    }
}
