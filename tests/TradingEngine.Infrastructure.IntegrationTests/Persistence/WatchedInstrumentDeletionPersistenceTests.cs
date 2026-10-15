using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class WatchedInstrumentDeletionPersistenceTests
{
    private const string Database = "Deletion";
    private static readonly Instant Created = Instant.FromUtc(2026, 1, 2, 9, 30);
    private static readonly Instant DeletedAt = Instant.FromUtc(2026, 10, 5, 9, 30);

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

        await using TradingEngineDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task DeleteInstrumentsAsync_WithValidToken_StampsDeletionAndRetainsHistory()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        byte[] token = await CurrentTokenAsync(id);

        Result<WatchedInstrumentBulkDeleteOutcome> outcome = await DeleteAsync(
            [new WatchedInstrumentDeleteItem(id, token)]);

        Assert.That(outcome.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Value.DeletedIds, Is.EqualTo(new[] { id }));
            Assert.That(outcome.Value.AlreadyDeletedIds, Is.Empty);
        });

        await using SqlConnection connection = new(ConnectionString());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT DeletedAt, MonitoringState, LastChangedAt, " +
            "(SELECT COUNT_BIG(1) FROM MonitoringRuleRevisions r " +
            "INNER JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
            "WHERE m.WatchedInstrumentId = @id) " +
            "FROM WatchedInstruments WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsDBNull(0), Is.False);
            Assert.That(reader.GetString(1), Is.EqualTo("Configured"));
            Assert.That(reader.GetDateTime(2), Is.EqualTo(DeletedAt.ToDateTimeUtc()));
            Assert.That(reader.GetInt64(3), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task DeleteInstrumentsAsync_WithUnknownId_ReturnsNotFoundAndDeletesNothing()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        Guid unknownId = Guid.Parse("e5000000-0000-0000-0000-000000000001");
        byte[] token = await CurrentTokenAsync(id);

        Result<WatchedInstrumentBulkDeleteOutcome> outcome = await DeleteAsync(
        [
            new WatchedInstrumentDeleteItem(id, token),
            new WatchedInstrumentDeleteItem(unknownId, [9, 9])
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsFailure, Is.True);
            Assert.That(
                outcome.Error.Code,
                Is.EqualTo("watched_instrument.delete_items_not_found"));
            Assert.That(
                outcome.Error.Extensions!["instrumentIds"],
                Is.EqualTo(new[] { unknownId }));
        });
        Assert.That(await IsDeletedAsync(id), Is.False);
    }

    [Test]
    public async Task DeleteInstrumentsAsync_WithOneStaleItem_RollsBackEntireBatch()
    {
        Guid first = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        Guid second = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        byte[] firstToken = await CurrentTokenAsync(first);
        byte[] secondToken = await CurrentTokenAsync(second);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentSettingsSnapshot> snapshot =
                await store.GetSettingsAsync(first, CancellationToken.None);
            WatchedInstrument instrument = snapshot.Value.Instrument;
            instrument.ChangeSamplingInterval(120, Instant.FromUtc(2026, 10, 4, 9, 30));
            Result<WatchedInstrumentSettingsSnapshot> saved = await store.SaveSettingsAsync(
                instrument,
                firstToken,
                CancellationToken.None);
            Assert.That(saved.IsSuccess, Is.True);
        }

        Result<WatchedInstrumentBulkDeleteOutcome> outcome = await DeleteAsync(
        [
            new WatchedInstrumentDeleteItem(first, firstToken),
            new WatchedInstrumentDeleteItem(second, secondToken)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsFailure, Is.True);
            Assert.That(
                outcome.Error.Code,
                Is.EqualTo("watched_instrument.delete_items_stale"));
            Assert.That(
                outcome.Error.Extensions!["instrumentIds"],
                Is.EqualTo(new[] { first }));
        });
        Assert.Multiple(async () =>
        {
            Assert.That(await IsDeletedAsync(first), Is.False);
            Assert.That(await IsDeletedAsync(second), Is.False);
        });
    }

    [Test]
    public async Task DeleteInstrumentsAsync_RepeatedOnDeletedId_IsIdempotentNoOp()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        byte[] token = await CurrentTokenAsync(id);

        Result<WatchedInstrumentBulkDeleteOutcome> first = await DeleteAsync(
            [new WatchedInstrumentDeleteItem(id, token)]);
        Result<WatchedInstrumentBulkDeleteOutcome> replay = await DeleteAsync(
            [new WatchedInstrumentDeleteItem(id, token)]);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(first.Value.DeletedIds, Is.EqualTo(new[] { id }));
            Assert.That(replay.IsSuccess, Is.True);
            Assert.That(replay.Value.DeletedIds, Is.Empty);
            Assert.That(replay.Value.AlreadyDeletedIds, Is.EqualTo(new[] { id }));
        });
    }

    [Test]
    public async Task DeleteInstrumentsAsync_ReplayAgainstOldId_LeavesNewRegistrationActive()
    {
        string symbol = $"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        Guid oldId = await AddInstrumentAsync(symbol);
        byte[] oldToken = await CurrentTokenAsync(oldId);
        await DeleteAsync([new WatchedInstrumentDeleteItem(oldId, oldToken)]);

        Guid newId = await AddInstrumentAsync(symbol);
        Assert.That(newId, Is.Not.EqualTo(oldId));

        Result<WatchedInstrumentBulkDeleteOutcome> replay = await DeleteAsync(
            [new WatchedInstrumentDeleteItem(oldId, oldToken)]);

        Assert.Multiple(() =>
        {
            Assert.That(replay.IsSuccess, Is.True);
            Assert.That(replay.Value.AlreadyDeletedIds, Is.EqualTo(new[] { oldId }));
        });
        Assert.That(await IsDeletedAsync(newId), Is.False);
    }

    [Test]
    public async Task AddAsync_SameBusinessKeyWhileActive_StillRejectedButSucceedsAfterDelete()
    {
        string symbol = $"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        Guid first = await AddInstrumentAsync(symbol);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<Guid> duplicate = await store.AddAsync(
                Registration(symbol),
                CancellationToken.None);
            Assert.That(duplicate.IsFailure, Is.True);
            Assert.That(duplicate.Error, Is.EqualTo(WatchedInstrumentErrors.DuplicateBusinessKey));
        }

        await DeleteAsync(
            [new WatchedInstrumentDeleteItem(first, await CurrentTokenAsync(first))]);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<Guid> reRegistered = await store.AddAsync(
                Registration(symbol),
                CancellationToken.None);
            Assert.That(reRegistered.IsSuccess, Is.True);
            Assert.That(reRegistered.Value, Is.Not.EqualTo(first));
        }
    }

    [Test]
    public async Task ReadsAfterDelete_TreatInstrumentAsNotFound()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        await DeleteAsync(
            [new WatchedInstrumentDeleteItem(id, await CurrentTokenAsync(id))]);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentPage> page = await store.SearchAsync(
                null,
                1,
                100,
                CancellationToken.None);
            Result<WatchedInstrumentConfiguration> configuration = await store.GetAsync(
                id,
                Instant.FromUtc(2026, 10, 6, 0, 0),
                CancellationToken.None);
            Result<WatchedInstrumentSettingsSnapshot> settings = await store.GetSettingsAsync(
                id,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(
                    page.Value.Items.Any(item => item.Id == id),
                    Is.False);
                Assert.That(configuration.Error, Is.EqualTo(WatchedInstrumentErrors.ConfigurationNotFound));
                Assert.That(settings.Error, Is.EqualTo(WatchedInstrumentErrors.SettingsNotFound));
            });
        }
    }

    [Test]
    public async Task SaveSettingsAsync_AfterDelete_ReturnsNotFound()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        byte[] token = await CurrentTokenAsync(id);
        WatchedInstrument instrument;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            instrument = (await store.GetSettingsAsync(id, CancellationToken.None))
                .Value.Instrument;
        }

        await DeleteAsync([new WatchedInstrumentDeleteItem(id, token)]);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            instrument.ChangeSamplingInterval(120, DeletedAt);
            Result<WatchedInstrumentSettingsSnapshot> saved = await store.SaveSettingsAsync(
                instrument,
                token,
                CancellationToken.None);

            Assert.That(saved.IsFailure, Is.True);
            Assert.That(saved.Error, Is.EqualTo(WatchedInstrumentErrors.SettingsNotFound));
        }
    }

    [Test]
    public async Task MonitoringRuleStore_AfterDelete_RejectsReadsAndWrites()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        MonitoringRuleSnapshot snapshot;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerMonitoringRuleStore rules = new(context, _serializer);
            Result<MonitoringRuleSnapshot> loaded = await rules.GetAsync(id, CancellationToken.None);
            Assert.That(loaded.IsSuccess, Is.True);
            snapshot = loaded.Value;
        }

        await DeleteAsync(
            [new WatchedInstrumentDeleteItem(id, await CurrentTokenAsync(id))]);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerMonitoringRuleStore rules = new(context, _serializer);
            Result<MonitoringRuleSnapshot> reloaded = await rules.GetAsync(
                id,
                CancellationToken.None);
            Result saved = await rules.SaveAsync(snapshot, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Error, Is.EqualTo(MonitoringRuleErrors.NotFound));
                Assert.That(saved.Error, Is.EqualTo(MonitoringRuleErrors.NotFound));
            });
        }
    }

    [Test]
    public async Task DeleteInstrumentsAsync_RacingRuleSave_SerializesWithoutCorruption()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        byte[] token = await CurrentTokenAsync(id);
        MonitoringRuleSnapshot snapshot;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerMonitoringRuleStore rules = new(context, _serializer);
            Result<MonitoringRuleSnapshot> loaded = await rules.GetAsync(
                id,
                CancellationToken.None);
            Assert.That(loaded.IsSuccess, Is.True);
            snapshot = loaded.Value;
        }

        await using TradingEngineDbContext saveContext = CreateContext();
        Task<Result> saving = new SqlServerMonitoringRuleStore(saveContext, _serializer)
            .SaveAsync(snapshot, CancellationToken.None);
        Task<Result<WatchedInstrumentBulkDeleteOutcome>> deleting = DeleteAsync(
            [new WatchedInstrumentDeleteItem(id, token)]);

        Result saved = await saving;
        Result<WatchedInstrumentBulkDeleteOutcome> deleted = await deleting;

        if (deleted.IsSuccess)
        {
            Assert.That(await IsDeletedAsync(id), Is.True);
        }
        else
        {
            Assert.That(await IsDeletedAsync(id), Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                saved.IsSuccess || saved.Error == MonitoringRuleErrors.NotFound,
                Is.True);
            Assert.That(saved.IsSuccess || deleted.IsSuccess, Is.True);
        });
    }

    [Test]
    public async Task SaveSettingsAsync_RacingDelete_OneOrderingWinsConsistently()
    {
        Guid id = await AddInstrumentAsync($"DEL-{Guid.NewGuid():N}"[..20].ToUpperInvariant());
        byte[] token = await CurrentTokenAsync(id);
        WatchedInstrument instrument;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            instrument = (await store.GetSettingsAsync(id, CancellationToken.None))
                .Value.Instrument;
        }

        instrument.ChangeSamplingInterval(120, DeletedAt);

        await using TradingEngineDbContext saveContext = CreateContext();
        Task<Result<WatchedInstrumentSettingsSnapshot>> saving =
            new SqlServerWatchedInstrumentStore(saveContext, _serializer)
                .SaveSettingsAsync(instrument, token, CancellationToken.None);
        Task<Result<WatchedInstrumentBulkDeleteOutcome>> deleting = DeleteAsync(
            [new WatchedInstrumentDeleteItem(id, token)]);

        Result<WatchedInstrumentSettingsSnapshot> saved = await saving;
        Result<WatchedInstrumentBulkDeleteOutcome> deleted = await deleting;

        Assert.Multiple(async () =>
        {
            Assert.That(saved.IsSuccess || deleted.IsSuccess, Is.True);
            if (deleted.IsSuccess)
            {
                Assert.That(await IsDeletedAsync(id), Is.True);
            }
            else
            {
                Assert.That(await IsDeletedAsync(id), Is.False);
                Assert.That(saved.IsSuccess, Is.True);
            }
        });
    }

    private async Task<Result<WatchedInstrumentBulkDeleteOutcome>> DeleteAsync(
        IReadOnlyList<WatchedInstrumentDeleteItem> items)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        return await store.DeleteInstrumentsAsync(items, DeletedAt, CancellationToken.None);
    }

    private async Task<byte[]> CurrentTokenAsync(Guid id)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        Result<WatchedInstrumentSettingsSnapshot> snapshot =
            await store.GetSettingsAsync(id, CancellationToken.None);
        Assert.That(snapshot.IsSuccess, Is.True);

        return snapshot.Value.ConcurrencyToken;
    }

    private async Task<bool> IsDeletedAsync(Guid id)
    {
        await using SqlConnection connection = new(ConnectionString());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT_BIG(1) FROM WatchedInstruments WHERE Id = @id AND DeletedAt IS NOT NULL";
        command.Parameters.AddWithValue("@id", id);

        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private async Task<Guid> AddInstrumentAsync(string symbol)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        Result<Guid> added = await store.AddAsync(
            Registration(symbol),
            CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);

        return added.Value;
    }

    private static WatchedInstrumentRegistration Registration(string symbol)
    {
        return new WatchedInstrumentRegistration(
            WatchedInstrument.Validate(symbol, "XTEST", "USD", 60).Value,
            MonitoringState.Configured,
            Created,
            CreateDefinition());
    }

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(ConnectionString())
                .Options;

        return new TradingEngineDbContext(options);
    }

    private string ConnectionString()
    {
        return $"{_connectionString};Database={Database}";
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
