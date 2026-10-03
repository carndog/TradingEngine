using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class MonitoringRulePersistenceTests
{
    private static readonly Guid UnknownInstrumentId = Guid.Parse("7d4a4f55-9c6e-4d2b-b3f1-5e8a9c1d2e34");
    private static readonly Instant January2 = Instant.FromUtc(2026, 1, 2, 9, 30);
    private static readonly Instant October1 = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant October5 = Instant.FromUtc(2026, 10, 5, 0, 0);
    private static readonly Instant October6 = Instant.FromUtc(2026, 10, 6, 0, 0);
    private static readonly Instant October10 = Instant.FromUtc(2026, 10, 10, 0, 0);
    private static readonly Instant October12 = Instant.FromUtc(2026, 10, 12, 0, 0);
    private const string Author = "synthetic-user";

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
    public async Task GetAsync_WithUnknownInstrument_ReturnsNotFound()
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);

        Result<MonitoringRuleSnapshot> result = await store.GetAsync(
            UnknownInstrumentId,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(MonitoringRuleErrors.NotFound));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        });
    }

    [Test]
    public async Task AddAsync_ThenGetAsync_RoundTripsCompleteAggregate()
    {
        Guid instrumentId = await AddBareInstrumentAsync("MR-ADD-1");
        MonitoringRule rule = MonitoringRule.Create(
            Guid.CreateVersion7(),
            instrumentId,
            October1).Value;
        rule.CreateDraft(
            RevisionId(11),
            CreateDefinition(100m),
            October1,
            Author,
            "initial zones",
            null);
        rule.ApplyNow(RevisionId(11), October1);
        rule.CreateDraft(
            RevisionId(12),
            CreateDefinition(110m),
            October5,
            Author,
            "raise support",
            new RevisionProposal(October6, October10));
        rule.CreateDraft(
            RevisionId(13),
            CreateDefinition(120m),
            October5,
            Author,
            "alternative",
            new RevisionProposal(October6, October10));

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerMonitoringRuleStore store = new(context, _serializer);
            Result added = await store.AddAsync(rule, CancellationToken.None);
            Assert.That(added.IsSuccess, Is.True);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerMonitoringRuleStore store = new(context, _serializer);
            Result<MonitoringRuleSnapshot> loaded = await store.GetAsync(
                instrumentId,
                CancellationToken.None);

            Assert.That(loaded.IsSuccess, Is.True);
            MonitoringRule restored = loaded.Value.Rule;
            Assert.Multiple(() =>
            {
                Assert.That(restored.Id, Is.EqualTo(rule.Id));
                Assert.That(restored.WatchedInstrumentId, Is.EqualTo(instrumentId));
                Assert.That(restored.CreatedAt, Is.EqualTo(October1));
                Assert.That(restored.Revisions, Has.Count.EqualTo(1));
                Assert.That(restored.Drafts, Has.Count.EqualTo(2));
                Assert.That(restored.Revisions[0].Id, Is.EqualTo(RevisionId(11)));
                Assert.That(restored.Revisions[0].RevisionNumber, Is.EqualTo(1));
                Assert.That(restored.Revisions[0].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October1));
                Assert.That(restored.Revisions[0].EffectivePeriod!.EffectiveTo, Is.Null);
                Assert.That(restored.Revisions[0].CreatedBy, Is.EqualTo(Author));
                Assert.That(restored.Revisions[0].ChangeReason, Is.EqualTo("initial zones"));
                Assert.That(
                    restored.Revisions[0].Definition.SupportZones[0].Level,
                    Is.EqualTo(100m));
                Assert.That(
                    restored.Drafts.Select(draft => draft.Id),
                    Is.EqualTo(new[] { RevisionId(12), RevisionId(13) }));
                Assert.That(
                    restored.Drafts[0].Proposal,
                    Is.EqualTo(new RevisionProposal(October6, October10)));
                Assert.That(restored.EffectiveAt(October1)!.Id, Is.EqualTo(RevisionId(11)));
                Assert.That(restored.EffectiveAt(October1 - Duration.FromTicks(1)), Is.Null);
                Assert.That(loaded.Value.ConcurrencyToken, Is.Not.Empty);
            });
        }
    }

    [Test]
    public async Task AddAsync_ForInstrumentWithExistingRule_ReturnsAlreadyExists()
    {
        Guid instrumentId = await AddBareInstrumentAsync("MR-ADD-2");

        Result first;
        Result second;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerMonitoringRuleStore store = new(context, _serializer);
            first = await store.AddAsync(
                MonitoringRule.Create(Guid.CreateVersion7(), instrumentId, October1).Value,
                CancellationToken.None);
            second = await store.AddAsync(
                MonitoringRule.Create(Guid.CreateVersion7(), instrumentId, October1).Value,
                CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsFailure, Is.True);
            Assert.That(second.Error, Is.EqualTo(MonitoringRuleErrors.AlreadyExists));
            Assert.That(second.Error.Type, Is.EqualTo(ErrorType.Conflict));
        });
    }

    [Test]
    public async Task SaveAsync_NewDraft_PersistsDraftAndAdvancesToken()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-DRAFT-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);

        snapshot.Rule.CreateDraft(
            RevisionId(21),
            CreateDefinition(110m),
            October5,
            Author,
            "candidate",
            new RevisionProposal(October10, null));

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Drafts, Has.Count.EqualTo(1));
            Assert.That(reloaded.Rule.Drafts[0].Id, Is.EqualTo(RevisionId(21)));
            Assert.That(
                reloaded.Rule.Drafts[0].Proposal,
                Is.EqualTo(new RevisionProposal(October10, null)));
            Assert.That(reloaded.Rule.Revisions, Has.Count.EqualTo(1));
            Assert.That(
                reloaded.ConcurrencyToken,
                Is.Not.EqualTo(snapshot.ConcurrencyToken));
        });
    }

    [Test]
    public async Task SaveAsync_OverlappingDraftProposals_PersistAndStayOutOfEffectiveLookup()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-DRAFT-2");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);

        snapshot.Rule.CreateDraft(
            RevisionId(22),
            CreateDefinition(110m),
            October5,
            Author,
            "alternative a",
            new RevisionProposal(October6, October10));
        snapshot.Rule.CreateDraft(
            RevisionId(23),
            CreateDefinition(120m),
            October5,
            Author,
            "alternative b",
            new RevisionProposal(October6, October10));

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Drafts, Has.Count.EqualTo(2));
            Assert.That(reloaded.Rule.EffectiveAt(October6), Is.Not.Null);
            Assert.That(
                reloaded.Rule.EffectiveAt(October6)!.Definition.SupportZones[0].Level,
                Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task SaveAsync_EditAndDeleteDraft_PersistChanges()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-DRAFT-3");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        seeded.Rule.CreateDraft(RevisionId(24), CreateDefinition(110m), October5, Author, "a", null);
        seeded.Rule.CreateDraft(RevisionId(25), CreateDefinition(120m), October5, Author, "b", null);
        await SaveAsync(seeded);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        snapshot.Rule.EditDraft(
            RevisionId(24),
            CreateDefinition(115m),
            "revised",
            new RevisionProposal(October12, null));
        snapshot.Rule.DeleteDraft(RevisionId(25));

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Drafts, Has.Count.EqualTo(1));
            Assert.That(reloaded.Rule.Drafts[0].Id, Is.EqualTo(RevisionId(24)));
            Assert.That(reloaded.Rule.Drafts[0].ChangeReason, Is.EqualTo("revised"));
            Assert.That(
                reloaded.Rule.Drafts[0].Definition.SupportZones[0].Level,
                Is.EqualTo(115m));
            Assert.That(
                reloaded.Rule.Drafts[0].Proposal,
                Is.EqualTo(new RevisionProposal(October12, null)));
            Assert.That(reloaded.Rule.FindRevision(RevisionId(25)), Is.Null);
        });
    }

    [Test]
    public async Task SaveAsync_ApplyNow_SplitsCurrentRevisionImmediately()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-APPLY-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Guid originalRevisionId = snapshot.Rule.Revisions[0].Id;

        snapshot.Rule.CreateDraft(
            RevisionId(31),
            CreateDefinition(110m),
            October5,
            Author,
            "raise support",
            null);
        snapshot.Rule.ApplyNow(RevisionId(31), October5);

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Revisions, Has.Count.EqualTo(2));
            Assert.That(reloaded.Rule.Revisions[0].Id, Is.EqualTo(originalRevisionId));
            Assert.That(reloaded.Rule.Revisions[0].EffectivePeriod!.EffectiveFrom, Is.EqualTo(January2));
            Assert.That(reloaded.Rule.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October5));
            Assert.That(reloaded.Rule.Revisions[0].RevisionNumber, Is.EqualTo(1));
            Assert.That(reloaded.Rule.Revisions[1].Id, Is.EqualTo(RevisionId(31)));
            Assert.That(reloaded.Rule.Revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October5));
            Assert.That(reloaded.Rule.Revisions[1].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(reloaded.Rule.Revisions[1].RevisionNumber, Is.EqualTo(2));
            Assert.That(reloaded.Rule.Drafts, Is.Empty);
            Assert.That(reloaded.Rule.EffectiveAt(October5 - Duration.FromTicks(1))!.Id, Is.EqualTo(originalRevisionId));
            Assert.That(reloaded.Rule.EffectiveAt(October5)!.Id, Is.EqualTo(RevisionId(31)));
        });
    }

    [Test]
    public async Task SaveAsync_ScheduleFutureRevision_PersistsBoundariesAndNumbers()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-SCHED-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Guid originalRevisionId = snapshot.Rule.Revisions[0].Id;

        snapshot.Rule.CreateDraft(
            RevisionId(32),
            CreateDefinition(110m),
            October5,
            Author,
            "raise support",
            null);
        snapshot.Rule.Schedule(RevisionId(32), October10, October5);

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Revisions, Has.Count.EqualTo(2));
            Assert.That(reloaded.Rule.Revisions[0].Id, Is.EqualTo(originalRevisionId));
            Assert.That(reloaded.Rule.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October10));
            Assert.That(reloaded.Rule.Revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October10));
            Assert.That(reloaded.Rule.Revisions[1].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(
                reloaded.Rule.Revisions.Select(revision => revision.RevisionNumber),
                Is.EqualTo(new int?[] { 1, 2 }));
            Assert.That(reloaded.Rule.EffectiveAt(October10)!.Id, Is.EqualTo(RevisionId(32)));
            Assert.That(reloaded.Rule.EffectiveAt(October10 - Duration.FromTicks(1))!.Id, Is.EqualTo(originalRevisionId));
        });
    }

    [Test]
    public async Task SaveAsync_SplitInsideTimeline_RenumbersAndPreservesLaterPeriods()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-SPLIT-1");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        seeded.Rule.CreateDraft(RevisionId(41), CreateDefinition(110m), October1, Author, "later", null);
        seeded.Rule.Schedule(RevisionId(41), October10, October1);
        await SaveAsync(seeded);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        snapshot.Rule.CreateDraft(RevisionId(42), CreateDefinition(105m), October1, Author, "middle", null);
        snapshot.Rule.Schedule(RevisionId(42), October5, October1);

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Revisions, Has.Count.EqualTo(3));
            Assert.That(
                reloaded.Rule.Revisions.Select(revision => revision.RevisionNumber),
                Is.EqualTo(new int?[] { 1, 2, 3 }));
            Assert.That(reloaded.Rule.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October5));
            Assert.That(reloaded.Rule.Revisions[1].Id, Is.EqualTo(RevisionId(42)));
            Assert.That(reloaded.Rule.Revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October5));
            Assert.That(reloaded.Rule.Revisions[1].EffectivePeriod!.EffectiveTo, Is.EqualTo(October10));
            Assert.That(reloaded.Rule.Revisions[2].Id, Is.EqualTo(RevisionId(41)));
            Assert.That(reloaded.Rule.Revisions[2].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October10));
            Assert.That(reloaded.Rule.Revisions[2].EffectivePeriod!.EffectiveTo, Is.Null);
        });
    }

    [Test]
    public async Task SaveAsync_RescheduleFutureRevision_MovesBoundaryAndPredecessorEnd()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-RESCHED-1");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        Guid originalRevisionId = seeded.Rule.Revisions[0].Id;
        seeded.Rule.CreateDraft(RevisionId(51), CreateDefinition(110m), October1, Author, "later", null);
        seeded.Rule.Schedule(RevisionId(51), October10, October1);
        await SaveAsync(seeded);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        snapshot.Rule.Reschedule(RevisionId(51), October12, October1);

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.FindRevision(originalRevisionId)!.EffectivePeriod!.EffectiveTo, Is.EqualTo(October12));
            Assert.That(reloaded.Rule.FindRevision(RevisionId(51))!.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October12));
            Assert.That(reloaded.Rule.EffectiveAt(October10)!.Id, Is.EqualTo(originalRevisionId));
            Assert.That(reloaded.Rule.EffectiveAt(October12)!.Id, Is.EqualTo(RevisionId(51)));
        });
    }

    [Test]
    public async Task SaveAsync_RemoveFutureRevision_DeletesRowAndReopensPredecessor()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-REMOVE-1");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        Guid originalRevisionId = seeded.Rule.Revisions[0].Id;
        seeded.Rule.CreateDraft(RevisionId(61), CreateDefinition(110m), October1, Author, "later", null);
        seeded.Rule.Schedule(RevisionId(61), October10, October1);
        await SaveAsync(seeded);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        snapshot.Rule.RemoveScheduledRevision(RevisionId(61), October1);

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Revisions, Has.Count.EqualTo(1));
            Assert.That(reloaded.Rule.FindRevision(RevisionId(61)), Is.Null);
            Assert.That(reloaded.Rule.FindRevision(originalRevisionId)!.EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(reloaded.Rule.EffectiveAt(October12)!.Id, Is.EqualTo(originalRevisionId));
        });
    }

    [Test]
    public async Task SaveAsync_EditFutureRevision_PersistsNewDefinitionXml()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-EDIT-1");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        seeded.Rule.CreateDraft(RevisionId(71), CreateDefinition(110m), October1, Author, "later", null);
        seeded.Rule.Schedule(RevisionId(71), October10, October1);
        await SaveAsync(seeded);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        snapshot.Rule.EditScheduledRevision(
            RevisionId(71),
            CreateDefinition(115m),
            "fine tune",
            October1);

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONVERT(nvarchar(max), DefinitionXml) FROM MonitoringRuleRevisions WHERE Id = @id";
        command.Parameters.AddWithValue("@id", RevisionId(71));

        string? storedXml = (string?)await command.ExecuteScalarAsync();

        Assert.That(storedXml, Is.Not.Null);
        ChartAnalysisDefinition persisted = _serializer.Deserialize(storedXml!);
        Assert.That(persisted.SupportZones[0].Level, Is.EqualTo(115m));
    }

    [Test]
    public async Task SaveAsync_WithNoChanges_LeavesPersistedStateIntact()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-NOOP-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Revisions, Has.Count.EqualTo(1));
            Assert.That(reloaded.Rule.Revisions[0].RevisionNumber, Is.EqualTo(1));
            Assert.That(reloaded.Rule.Drafts, Is.Empty);
        });
    }

    [Test]
    public async Task Schema_MonitoringRuleColumns_HaveExpectedSqlTypes()
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        object? xmlType = await ScalarAsync(
            connection,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = 'MonitoringRuleRevisions' AND COLUMN_NAME = 'DefinitionXml'");
        object? versionType = await ScalarAsync(
            connection,
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = 'MonitoringRules' AND COLUMN_NAME = 'RowVersion'");

        Assert.Multiple(() =>
        {
            Assert.That(xmlType, Is.EqualTo("xml"));
            Assert.That(versionType, Is.EqualTo("timestamp"));
        });
    }

    [Test]
    public async Task GetWatchedInstrument_AtAndBeforeFirstRevisionStart_ResolvesApplicability()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-APPLY-BND");

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        Result<WatchedInstrumentConfiguration> at = await store.GetAsync(
            instrumentId,
            January2,
            CancellationToken.None);
        Result<WatchedInstrumentConfiguration> after = await store.GetAsync(
            instrumentId,
            January2 + Duration.FromTicks(1),
            CancellationToken.None);
        Result<WatchedInstrumentConfiguration> before = await store.GetAsync(
            instrumentId,
            January2 - Duration.FromTicks(1),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(at.IsSuccess, Is.True);
            Assert.That(after.IsSuccess, Is.True);
            Assert.That(before.IsFailure, Is.True);
            Assert.That(before.Error, Is.EqualTo(MonitoringRuleErrors.NoApplicableRevision));
            Assert.That(before.Error.Type, Is.EqualTo(ErrorType.NotFound));
        });
    }

    private async Task<Guid> AddInstrumentAsync(string symbol)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        WatchedInstrumentFields fields = WatchedInstrument
            .Validate(symbol, "XTEST", "USD", 60)
            .Value;
        WatchedInstrumentRegistration registration = new(
            fields,
            MonitoringState.Configured,
            January2,
            CreateDefinition(100m));

        Result<Guid> added = await store.AddAsync(registration, CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);

        return added.Value;
    }

    private async Task<Guid> AddBareInstrumentAsync(string symbol)
    {
        Guid instrumentId = Guid.CreateVersion7();
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO WatchedInstruments " +
            "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, " +
            "CreatedAt, LastChangedAt) " +
            "VALUES (@id, @symbol, N'XTEST', N'USD', N'Configured', 60, " +
            "CONVERT(datetime2(7), '2026-01-02T09:30:00'), " +
            "CONVERT(datetime2(7), '2026-01-02T09:30:00'))";
        command.Parameters.AddWithValue("@id", instrumentId);
        command.Parameters.AddWithValue("@symbol", symbol);
        await command.ExecuteNonQueryAsync();

        return instrumentId;
    }

    private async Task<MonitoringRuleSnapshot> LoadAsync(Guid instrumentId)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);
        Result<MonitoringRuleSnapshot> loaded = await store.GetAsync(
            instrumentId,
            CancellationToken.None);
        Assert.That(loaded.IsSuccess, Is.True);

        return loaded.Value;
    }

    private async Task<Result> SaveAsync(MonitoringRuleSnapshot snapshot)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);

        return await store.SaveAsync(snapshot, CancellationToken.None);
    }

    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync();
    }

    private static Guid RevisionId(int ordinal)
    {
        return Guid.Parse($"b2000000-0000-0000-0000-{ordinal:D12}");
    }

    private static ChartAnalysisDefinition CreateDefinition(decimal supportLevel)
    {
        return ChartAnalysisDefinition.Create(
            4,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    supportLevel - 5m,
                    supportLevel,
                    supportLevel + 5m,
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

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options = new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new TradingEngineDbContext(options);
    }
}
