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
public sealed class MonitoringRuleStagingTests
{
    private static readonly Instant October1 = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant October5 = Instant.FromUtc(2026, 10, 5, 0, 0);
    private static readonly Instant October10 = Instant.FromUtc(2026, 10, 10, 0, 0);
    private static readonly Instant October15 = Instant.FromUtc(2026, 10, 15, 0, 0);
    private static readonly Instant October18 = Instant.FromUtc(2026, 10, 18, 0, 0);
    private static readonly Instant October20 = Instant.FromUtc(2026, 10, 20, 0, 0);
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
    public async Task SaveAsync_RemovedTailAndRescheduledBoundary_PersistsReconciledTimeline()
    {
        Guid instrumentId = await SeedTimelineAsync(
            "MR-STAGE-1",
            [
                Committed(1, 100, October5, October10),
                Committed(2, 110, October10, October15),
                Committed(3, 120, October15, null)
            ]);
        Guid revA = RevisionId(instrumentId, 1);
        Guid revB = RevisionId(instrumentId, 2);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Result removed = snapshot.Rule.RemoveScheduledRevision(RevisionId(instrumentId, 3), October1);
        Result rescheduled = snapshot.Rule.Reschedule(revB, October20, October1);
        Assert.Multiple(() =>
        {
            Assert.That(removed.IsSuccess, Is.True);
            Assert.That(rescheduled.IsSuccess, Is.True);
        });

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);
        MonitoringRule reloaded = await ReloadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Revisions, Has.Count.EqualTo(2));
            Assert.That(reloaded.Revisions[0].Id, Is.EqualTo(revA));
            Assert.That(reloaded.Revisions[1].Id, Is.EqualTo(revB));
            Assert.That(reloaded.Revisions[0].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October5));
            Assert.That(reloaded.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October20));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October20));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(reloaded.EffectiveAt(October20 - Duration.FromNanoseconds(1))!.Id, Is.EqualTo(revA));
            Assert.That(reloaded.EffectiveAt(October20)!.Id, Is.EqualTo(revB));
        });
    }

    [Test]
    public async Task SaveAsync_AdjacentReschedulesReusingVacatedStart_PersistsFinalChain()
    {
        Guid instrumentId = await SeedTimelineAsync(
            "MR-STAGE-2",
            [
                Committed(1, 100, October5, October10),
                Committed(2, 110, October10, October15),
                Committed(3, 120, October15, October20),
                Committed(4, 130, October20, null)
            ]);
        Guid revA = RevisionId(instrumentId, 1);
        Guid revB = RevisionId(instrumentId, 2);
        Guid revC = RevisionId(instrumentId, 3);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Result firstMove = snapshot.Rule.Reschedule(revC, October18, October1);
        Result secondMove = snapshot.Rule.Reschedule(revB, October15, October1);
        Assert.Multiple(() =>
        {
            Assert.That(firstMove.IsSuccess, Is.True);
            Assert.That(secondMove.IsSuccess, Is.True);
        });

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);
        MonitoringRule reloaded = await ReloadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Revisions, Has.Count.EqualTo(4));
            Assert.That(reloaded.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October15));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October15));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.EffectiveTo, Is.EqualTo(October18));
            Assert.That(reloaded.Revisions[2].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October18));
            Assert.That(reloaded.Revisions[2].EffectivePeriod!.EffectiveTo, Is.EqualTo(October20));
            Assert.That(reloaded.Revisions[3].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October20));
            Assert.That(reloaded.EffectiveAt(October15 - Duration.FromNanoseconds(1))!.Id, Is.EqualTo(revA));
            Assert.That(reloaded.EffectiveAt(October15)!.Id, Is.EqualTo(revB));
            Assert.That(reloaded.EffectiveAt(October18)!.Id, Is.EqualTo(revC));
        });
    }

    [Test]
    public async Task SaveAsync_RemovalThenRescheduleIntoVacatedStart_PreservesBoundaryEquality()
    {
        Guid instrumentId = await SeedTimelineAsync(
            "MR-STAGE-3",
            [
                Committed(1, 100, October5, October10),
                Committed(2, 110, October10, October15),
                Committed(3, 120, October15, null)
            ]);
        Guid revA = RevisionId(instrumentId, 1);
        Guid revC = RevisionId(instrumentId, 3);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Result removed = snapshot.Rule.RemoveScheduledRevision(RevisionId(instrumentId, 2), October1);
        Result rescheduled = snapshot.Rule.Reschedule(revC, October10, October1);
        Assert.Multiple(() =>
        {
            Assert.That(removed.IsSuccess, Is.True);
            Assert.That(rescheduled.IsSuccess, Is.True);
        });

        Result saved = await SaveAsync(snapshot);

        Assert.That(saved.IsSuccess, Is.True);
        MonitoringRule reloaded = await ReloadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Revisions, Has.Count.EqualTo(2));
            Assert.That(reloaded.Revisions[0].Id, Is.EqualTo(revA));
            Assert.That(reloaded.Revisions[0].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October5));
            Assert.That(reloaded.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October10));
            Assert.That(reloaded.Revisions[1].Id, Is.EqualTo(revC));
            Assert.That(reloaded.Revisions[1].RevisionNumber, Is.EqualTo(2));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October10));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(reloaded.EffectiveAt(October10 - Duration.FromNanoseconds(1))!.Id, Is.EqualTo(revA));
            Assert.That(reloaded.EffectiveAt(October10)!.Id, Is.EqualTo(revC));
        });
    }

    [Test]
    public async Task SaveAsync_WhenCombinedMutationFailsMidTransaction_RollsBackWithdrawals()
    {
        Guid instrumentId = await SeedTimelineAsync(
            "MR-STAGE-4",
            [
                Committed(1, 100, October5, October10),
                Committed(2, 110, October10, October15),
                Committed(3, 120, October15, null)
            ]);
        byte[] versionBefore = await ReadRowVersionAsync(instrumentId);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        snapshot.Rule.RemoveScheduledRevision(RevisionId(instrumentId, 3), October1);
        snapshot.Rule.Reschedule(RevisionId(instrumentId, 2), October20, October1);
        snapshot.Rule.CreateDraft(
            RevisionId(instrumentId, 4),
            CreateDefinition(130),
            October1,
            new string('x', 200),
            "rolled back",
            null);

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(snapshot));

        byte[] versionAfter = await ReadRowVersionAsync(instrumentId);
        MonitoringRule reloaded = await ReloadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(versionAfter, Is.EqualTo(versionBefore));
            Assert.That(reloaded.Revisions, Has.Count.EqualTo(3));
            Assert.That(reloaded.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October10));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October10));
            Assert.That(reloaded.Revisions[1].EffectivePeriod!.EffectiveTo, Is.EqualTo(October15));
            Assert.That(reloaded.Revisions[2].EffectivePeriod!.EffectiveFrom, Is.EqualTo(October15));
            Assert.That(reloaded.Revisions[2].EffectivePeriod!.IsOpenEnded, Is.True);
        });
    }

    [Test]
    public async Task GetAsync_AroundSharedBoundaryAfterPersistence_SelectsSuccessorExactly()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-BND-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Guid successor = RevisionId(instrumentId, 2);
        snapshot.Rule.CreateDraft(successor, CreateDefinition(110), October1, Author, "successor", null);
        snapshot.Rule.Schedule(successor, October10, null, RevisionId(instrumentId, 90), October1);
        Result saved = await SaveAsync(snapshot);
        Assert.That(saved.IsSuccess, Is.True);

        Result<WatchedInstrumentConfiguration> before = await GetConfigurationAsync(
            instrumentId,
            October10 - Duration.FromNanoseconds(1));
        Result<WatchedInstrumentConfiguration> at = await GetConfigurationAsync(
            instrumentId,
            October10);
        Result<WatchedInstrumentConfiguration> after = await GetConfigurationAsync(
            instrumentId,
            October10 + Duration.FromNanoseconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(before.IsSuccess, Is.True);
            Assert.That(before.Value.Definition.SupportZones[0].Level, Is.EqualTo(100m));
            Assert.That(at.IsSuccess, Is.True);
            Assert.That(at.Value.Definition.SupportZones[0].Level, Is.EqualTo(110m));
            Assert.That(after.IsSuccess, Is.True);
            Assert.That(after.Value.Definition.SupportZones[0].Level, Is.EqualTo(110m));
        });
    }

    [Test]
    public async Task GetAsync_MinimumStoredPeriod_ResolvesInsideAndAtEnd()
    {
        Instant start = October10;
        Instant end = start.PlusTicks(1);
        Guid instrumentId = await SeedTimelineAsync(
            "MR-BND-2",
            [
                Committed(1, 100, October5, start),
                Committed(2, 110, start, end),
                Committed(3, 120, end, null)
            ]);

        Result<WatchedInstrumentConfiguration> beforeStart = await GetConfigurationAsync(
            instrumentId,
            start - Duration.FromNanoseconds(1));
        Result<WatchedInstrumentConfiguration> atStart = await GetConfigurationAsync(
            instrumentId,
            start);
        Result<WatchedInstrumentConfiguration> inside = await GetConfigurationAsync(
            instrumentId,
            start + Duration.FromNanoseconds(50));
        Result<WatchedInstrumentConfiguration> lastNanosecond = await GetConfigurationAsync(
            instrumentId,
            end - Duration.FromNanoseconds(1));
        Result<WatchedInstrumentConfiguration> atEnd = await GetConfigurationAsync(
            instrumentId,
            end);
        MonitoringRule reloaded = await ReloadAsync(instrumentId);

        Assert.Multiple(() =>
        {
            Assert.That(beforeStart.Value.Definition.SupportZones[0].Level, Is.EqualTo(100m));
            Assert.That(atStart.Value.Definition.SupportZones[0].Level, Is.EqualTo(110m));
            Assert.That(inside.Value.Definition.SupportZones[0].Level, Is.EqualTo(110m));
            Assert.That(lastNanosecond.Value.Definition.SupportZones[0].Level, Is.EqualTo(110m));
            Assert.That(atEnd.Value.Definition.SupportZones[0].Level, Is.EqualTo(120m));
            Assert.That(
                reloaded.Revisions[1].EffectivePeriod!.EffectiveTo,
                Is.EqualTo(end));
        });
    }

    [Test]
    public async Task AddAsync_DraftOnlyRule_PersistsDraftAndResolvesNothing()
    {
        Guid instrumentId = await SeedTimelineAsync(
            "MR-DRAFT-1",
            [
                new RestoredRevision<ChartAnalysisDefinition>(
                    Guid.Empty,
                    CreateDefinition(100),
                    October1,
                    Author,
                    null,
                    null,
                    null,
                    null,
                    October10,
                    null)
            ]);

        Result<WatchedInstrumentConfiguration> configuration = await GetConfigurationAsync(
            instrumentId,
            October15);
        MonitoringRule reloaded = await ReloadAsync(instrumentId);

        Assert.Multiple(() =>
        {
            Assert.That(configuration.IsFailure, Is.True);
            Assert.That(configuration.Error, Is.EqualTo(MonitoringRuleErrors.NoApplicableRevision));
            Assert.That(reloaded.Revisions, Is.Empty);
            Assert.That(reloaded.Drafts, Has.Count.EqualTo(1));
            Assert.That(
                reloaded.Drafts[0].Proposal,
                Is.EqualTo(new RevisionProposal(October10, null)));
        });
    }

    [Test]
    public async Task GetAsync_WithCommittedPeriodOverlap_ThrowsInvalidData()
    {
        Guid instrumentId = await AddInstrumentAsync("MR-CORRUPT-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Guid successor = RevisionId(instrumentId, 2);
        snapshot.Rule.CreateDraft(successor, CreateDefinition(110), October1, Author, "successor", null);
        snapshot.Rule.Schedule(successor, October10, null, RevisionId(instrumentId, 91), October1);
        Result saved = await SaveAsync(snapshot);
        Assert.That(saved.IsSuccess, Is.True);

        await using (SqlConnection connection = new(_connectionString))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE r SET r.EffectiveTo = @overlap " +
                "FROM MonitoringRuleRevisions r " +
                "JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
                "WHERE m.WatchedInstrumentId = @id AND r.EffectiveTo IS NOT NULL";
            command.Parameters.AddWithValue("@id", instrumentId);
            command.Parameters.Add(new SqlParameter("@overlap", System.Data.SqlDbType.DateTime2)
            {
                Value = October10.PlusTicks(1).ToDateTimeUtc()
            });
            int corrupted = await command.ExecuteNonQueryAsync();
            Assert.That(corrupted, Is.EqualTo(1));
        }

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.GetAsync(instrumentId, CancellationToken.None));
    }

    private async Task<Guid> AddInstrumentAsync(string symbol)
    {
        WatchedInstrumentFields fields = WatchedInstrument
            .Validate(symbol, "XTEST", "USD", 60)
            .Value;
        WatchedInstrumentRegistration registration = new(
            fields,
            MonitoringState.Configured,
            October1,
            CreateDefinition(100));

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        Result<Guid> result = await store.AddAsync(registration, CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True);

        return result.Value;
    }

    private async Task<Guid> SeedTimelineAsync(
        string symbol,
        RestoredRevision<ChartAnalysisDefinition>[] revisions)
    {
        Guid instrumentId = await AddBareInstrumentAsync(symbol);
        RestoredRevision<ChartAnalysisDefinition>[] assigned = revisions
            .Select((revision, index) => revision with { Id = RevisionId(instrumentId, index + 1) })
            .ToArray();
        MonitoringRule rule = MonitoringRule.Restore(
            Guid.CreateVersion7(),
            instrumentId,
            October1,
            assigned).Value;

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);
        Result added = await store.AddAsync(rule, CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);

        return instrumentId;
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
            "VALUES (@id, @symbol, N'XTEST', N'USD', N'Configured', 60, @at, @at)";
        command.Parameters.AddWithValue("@id", instrumentId);
        command.Parameters.AddWithValue("@symbol", symbol);
        command.Parameters.AddWithValue("@at", October1.ToDateTimeUtc());
        await command.ExecuteNonQueryAsync();

        return instrumentId;
    }

    private async Task<MonitoringRuleSnapshot> LoadAsync(Guid instrumentId)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);
        Result<MonitoringRuleSnapshot> result = await store.GetAsync(
            instrumentId,
            CancellationToken.None);
        Assert.That(result.IsSuccess, Is.True);

        return result.Value;
    }

    private async Task<MonitoringRule> ReloadAsync(Guid instrumentId)
    {
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);

        return snapshot.Rule;
    }

    private async Task<Result> SaveAsync(MonitoringRuleSnapshot snapshot)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);

        return await store.SaveAsync(snapshot, CancellationToken.None);
    }

    private async Task<Result<WatchedInstrumentConfiguration>> GetConfigurationAsync(
        Guid instrumentId,
        Instant at)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        return await store.GetAsync(instrumentId, at, CancellationToken.None);
    }

    private async Task<byte[]> ReadRowVersionAsync(Guid instrumentId)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.RowVersion FROM MonitoringRules m WHERE m.WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);

        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options = new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new TradingEngineDbContext(options);
    }

    private static RestoredRevision<ChartAnalysisDefinition> Committed(
        int number,
        int level,
        Instant effectiveFrom,
        Instant? effectiveTo)
    {
        return new RestoredRevision<ChartAnalysisDefinition>(
            Guid.Empty,
            CreateDefinition(level),
            October1,
            Author,
            null,
            number,
            effectiveFrom,
            effectiveTo,
            null,
            null);
    }

    private static Guid RevisionId(Guid instrumentId, int ordinal)
    {
        byte[] bytes = instrumentId.ToByteArray();
        bytes[15] = (byte)ordinal;

        return new Guid(bytes);
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
}
