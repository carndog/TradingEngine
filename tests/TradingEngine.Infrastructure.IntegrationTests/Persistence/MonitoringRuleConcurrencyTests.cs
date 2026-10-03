using System.Xml.Schema;
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
public sealed class MonitoringRuleConcurrencyTests
{
    private static readonly Instant January2 = Instant.FromUtc(2026, 1, 2, 9, 30);
    private static readonly Instant October1 = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant October5 = Instant.FromUtc(2026, 10, 5, 0, 0);
    private static readonly Instant October10 = Instant.FromUtc(2026, 10, 10, 0, 0);
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
    public async Task SaveAsync_WithStaleToken_ReturnsConcurrentChangeAndLeavesStateIntact()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-STALE-1");
        MonitoringRuleSnapshot stale = await LoadAsync(instrumentId);
        MonitoringRuleSnapshot fresh = await LoadAsync(instrumentId);

        fresh.Rule.CreateDraft(RevisionId(81), CreateDefinition(110m), October5, Author, "first", null);
        Result firstSave = await SaveAsync(fresh);
        Assert.That(firstSave.IsSuccess, Is.True);

        stale.Rule.CreateDraft(RevisionId(82), CreateDefinition(120m), October5, Author, "stale", null);
        Result staleSave = await SaveAsync(stale);

        Assert.Multiple(() =>
        {
            Assert.That(staleSave.IsFailure, Is.True);
            Assert.That(staleSave.Error, Is.EqualTo(MonitoringRuleErrors.ConcurrentChange));
            Assert.That(staleSave.Error.Type, Is.EqualTo(ErrorType.Conflict));
        });

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Drafts, Has.Count.EqualTo(1));
            Assert.That(reloaded.Rule.Drafts[0].Id, Is.EqualTo(RevisionId(81)));
            Assert.That(reloaded.Rule.FindRevision(RevisionId(82)), Is.Null);
        });
    }

    [Test]
    public async Task SaveAsync_CompetingDraftEdits_RejectStaleSecondWriter()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-EDIT-1");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        seeded.Rule.CreateDraft(RevisionId(83), CreateDefinition(110m), October5, Author, "draft", null);
        await SaveAsync(seeded);

        MonitoringRuleSnapshot first = await LoadAsync(instrumentId);
        MonitoringRuleSnapshot second = await LoadAsync(instrumentId);

        first.Rule.EditDraft(RevisionId(83), CreateDefinition(111m), "writer a", null);
        second.Rule.EditDraft(RevisionId(83), CreateDefinition(122m), "writer b", null);

        Result firstSave = await SaveAsync(first);
        Result secondSave = await SaveAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(firstSave.IsSuccess, Is.True);
            Assert.That(secondSave.IsFailure, Is.True);
            Assert.That(secondSave.Error, Is.EqualTo(MonitoringRuleErrors.ConcurrentChange));
        });

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Drafts[0].Definition.SupportZones[0].Level, Is.EqualTo(111m));
            Assert.That(reloaded.Rule.Drafts[0].ChangeReason, Is.EqualTo("writer a"));
        });
    }

    [Test]
    public async Task SaveAsync_CompetingCommitsOfDifferentDrafts_RejectStaleSecondWriter()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-COMMIT-1");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        seeded.Rule.CreateDraft(RevisionId(84), CreateDefinition(110m), October5, Author, "a", null);
        seeded.Rule.CreateDraft(RevisionId(85), CreateDefinition(120m), October5, Author, "b", null);
        await SaveAsync(seeded);

        MonitoringRuleSnapshot first = await LoadAsync(instrumentId);
        MonitoringRuleSnapshot second = await LoadAsync(instrumentId);

        first.Rule.Schedule(RevisionId(84), October10, October5);
        second.Rule.Schedule(RevisionId(85), October10, October5);

        Result firstSave = await SaveAsync(first);
        Result secondSave = await SaveAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(firstSave.IsSuccess, Is.True);
            Assert.That(secondSave.IsFailure, Is.True);
            Assert.That(secondSave.Error, Is.EqualTo(MonitoringRuleErrors.ConcurrentChange));
        });

        MonitoringRuleSnapshot reloaded = await LoadAsync(instrumentId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Rule.Revisions, Has.Count.EqualTo(2));
            Assert.That(reloaded.Rule.Revisions[1].Id, Is.EqualTo(RevisionId(84)));
            Assert.That(reloaded.Rule.FindRevision(RevisionId(85))!.IsDraft, Is.True);
        });
    }

    [Test]
    public async Task SaveAsync_WhenAStagedWriteFailsMidTransaction_RollsBackWholeMutation()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-ROLL-1");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        seeded.Rule.CreateDraft(RevisionId(86), CreateDefinition(110m), October5, Author, "victim", null);
        seeded.Rule.CreateDraft(RevisionId(87), CreateDefinition(120m), October5, Author, "future split", null);
        seeded.Rule.Schedule(RevisionId(87), October10, October5);
        await SaveAsync(seeded);

        long versionBefore = await ReadRowVersionAsync(instrumentId);

        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);
        Result rescheduled = snapshot.Rule.Reschedule(
            RevisionId(87),
            October5 + Duration.FromHours(2),
            October1);
        Result<Revision<ChartAnalysisDefinition>> drafted = snapshot.Rule.CreateDraft(
            RevisionId(88),
            CreateDefinition(130m),
            October5,
            Author,
            "rolled back",
            null);
        Result edited = snapshot.Rule.EditDraft(
            RevisionId(86),
            CreateDefinition(115m),
            new string('r', 600),
            null);

        Assert.Multiple(() =>
        {
            Assert.That(rescheduled.IsSuccess, Is.True);
            Assert.That(drafted.IsSuccess, Is.True);
            Assert.That(edited.IsSuccess, Is.True);
        });

        await Assert.CatchAsync<DbUpdateException>(() => SaveAsync(snapshot));

        long versionAfter = await ReadRowVersionAsync(instrumentId);
        int rowCount;
        int originalStartRows;
        int originalReasonRows;
        await using (TradingEngineDbContext context = CreateContext())
        {
            rowCount = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM MonitoringRuleRevisions r JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id WHERE m.WatchedInstrumentId = {instrumentId}")
                .SingleAsync();
            originalStartRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM MonitoringRuleRevisions WHERE Id = {RevisionId(87)} AND EffectiveFrom = CONVERT(datetime2(7), '2026-10-10T00:00:00')")
                .SingleAsync();
            originalReasonRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM MonitoringRuleRevisions WHERE Id = {RevisionId(86)} AND ChangeReason = N'victim'")
                .SingleAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(versionAfter, Is.EqualTo(versionBefore));
            Assert.That(rowCount, Is.EqualTo(3));
            Assert.That(originalStartRows, Is.EqualTo(1));
            Assert.That(originalReasonRows, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SaveAsync_WithSubTickInstant_ThrowsArgumentOutOfRange()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-TICK-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);

        snapshot.Rule.CreateDraft(
            RevisionId(89),
            CreateDefinition(110m),
            October5.PlusNanoseconds(50),
            Author,
            "fine precision",
            null);

        await Assert.CatchAsync<ArgumentOutOfRangeException>(
            () => SaveAsync(snapshot));
    }

    [Test]
    public async Task SaveAsync_WhenCancelled_PropagatesCancellation()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-CANCEL-1");
        MonitoringRuleSnapshot snapshot = await LoadAsync(instrumentId);

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);
        CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(
            () => store.SaveAsync(snapshot, cancellation.Token));
    }

    [Test]
    public async Task GetAsync_WithSchemaInvalidXml_ThrowsXmlSchemaValidation()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-CORRUPT-XML");
        await ExecuteSqlAsync(
            "UPDATE MonitoringRuleRevisions SET DefinitionXml = " +
            "N'<ChartAnalysisDefinition priceScale=\"99\"/>' " +
            "WHERE Id IN (SELECT r.Id FROM MonitoringRuleRevisions r " +
            "JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
            "WHERE m.WatchedInstrumentId = @id)",
            instrumentId);

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);

        await Assert.ThrowsAsync<XmlSchemaValidationException>(
            () => store.GetAsync(instrumentId, CancellationToken.None));
    }

    [Test]
    public async Task GetAsync_WithCommittedPeriodGap_ThrowsInvalidData()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-CORRUPT-GAP");
        MonitoringRuleSnapshot seeded = await LoadAsync(instrumentId);
        seeded.Rule.CreateDraft(RevisionId(90), CreateDefinition(110m), October1, Author, "later", null);
        seeded.Rule.Schedule(RevisionId(90), October10, October1);
        await SaveAsync(seeded);

        await ExecuteSqlAsync(
            "UPDATE MonitoringRuleRevisions SET EffectiveFrom = '2026-10-12T00:00:00' " +
            "WHERE Id = @id",
            RevisionId(90));

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.GetAsync(instrumentId, CancellationToken.None));
    }

    [Test]
    public async Task GetAsync_WithOutOfPositionNumber_ThrowsInvalidData()
    {
        Guid instrumentId = await AddInstrumentAsync("MC-CORRUPT-NUM");
        await ExecuteSqlAsync(
            "UPDATE MonitoringRuleRevisions SET RevisionNumber = 9 " +
            "WHERE Id IN (SELECT r.Id FROM MonitoringRuleRevisions r " +
            "JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
            "WHERE m.WatchedInstrumentId = @id)",
            instrumentId);

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.GetAsync(instrumentId, CancellationToken.None));
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

    private async Task<long> ReadRowVersionAsync(Guid instrumentId)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONVERT(bigint, RowVersion) FROM MonitoringRules WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteSqlAsync(string sql, Guid parameterValue)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", parameterValue);
        await command.ExecuteNonQueryAsync();
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
