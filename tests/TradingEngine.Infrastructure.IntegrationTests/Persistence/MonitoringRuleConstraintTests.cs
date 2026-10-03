using System.Data;
using Microsoft.Data.SqlClient;
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
public sealed class MonitoringRuleConstraintTests
{
    private static readonly Instant January2 = Instant.FromUtc(2026, 1, 2, 9, 30);
    private static readonly Instant February1 = Instant.FromUtc(2026, 2, 1, 0, 0);
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
    public async Task SqlConstraints_MultipleDraftsWithNullCommittedState_AreAccepted()
    {
        Guid ruleId = await SeedRuleAsync("CT-DRAFTS-1");
        Guid firstId = Guid.CreateVersion7();
        Guid secondId = Guid.CreateVersion7();

        await InsertRevisionAsync(ruleId, firstId, null, null, null, null, null);
        await InsertRevisionAsync(ruleId, secondId, null, null, null, October10, October5);

        Assert.That(await CountRevisionsAsync(ruleId), Is.EqualTo(3));
    }

    [Test]
    public async Task SqlConstraints_DraftWithEffectiveEndButNoStart_IsRejected()
    {
        Guid ruleId = await SeedRuleAsync("CT-DRAFT-END");

        SqlException? exception = Assert.CatchAsync<SqlException>(
            () => InsertRevisionAsync(
                ruleId, Guid.CreateVersion7(), null, null, October5, null, null));
        int revisions = await CountRevisionsAsync(ruleId);

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(547));
            Assert.That(revisions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SqlConstraints_CommittedRevisionWithoutNumber_IsRejected()
    {
        Guid ruleId = await SeedRuleAsync("CT-NO-NUM");

        SqlException? exception = Assert.CatchAsync<SqlException>(
            () => InsertRevisionAsync(
                ruleId, Guid.CreateVersion7(), null, October5, October10, null, null));
        int revisions = await CountRevisionsAsync(ruleId);

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(547));
            Assert.That(revisions, Is.EqualTo(1));
        });
    }

    [Test]
    [TestCase(0)]
    [TestCase(-3)]
    public async Task SqlConstraints_CommittedRevisionWithNonPositiveNumber_IsRejected(int number)
    {
        Guid ruleId = await SeedRuleAsync($"CT-NUM-{number}");

        SqlException? exception = Assert.CatchAsync<SqlException>(
            () => InsertRevisionAsync(
                ruleId, Guid.CreateVersion7(), number, October5, October10, null, null));
        int revisions = await CountRevisionsAsync(ruleId);

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(547));
            Assert.That(revisions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SqlConstraints_CommittedRevisionWithEqualBoundaries_IsRejected()
    {
        Guid ruleId = await SeedRuleAsync("CT-EQUAL");

        SqlException? exception = Assert.CatchAsync<SqlException>(
            () => InsertRevisionAsync(
                ruleId, Guid.CreateVersion7(), 2, October5, October5, null, null));
        int revisions = await CountRevisionsAsync(ruleId);

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(547));
            Assert.That(revisions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SqlConstraints_CommittedRevisionWithInvertedBoundaries_IsRejected()
    {
        Guid ruleId = await SeedRuleAsync("CT-INVERTED");

        SqlException? exception = Assert.CatchAsync<SqlException>(
            () => InsertRevisionAsync(
                ruleId, Guid.CreateVersion7(), 2, October10, October5, null, null));
        int revisions = await CountRevisionsAsync(ruleId);

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(547));
            Assert.That(revisions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SqlConstraints_ClosedAndOpenEndedCommittedPeriods_AreAccepted()
    {
        Guid ruleId = await SeedRuleAsync("CT-PERIODS");

        await InsertRevisionAsync(
            ruleId, Guid.CreateVersion7(), 2, October5, October10, null, null);
        await InsertRevisionAsync(
            ruleId, Guid.CreateVersion7(), 3, February1, null, null, null);

        Assert.That(await CountRevisionsAsync(ruleId), Is.EqualTo(3));
    }

    [Test]
    public async Task SqlConstraints_DeletingInstrumentWithRuleAndHistory_FailsAndPreservesEverything()
    {
        Guid instrumentId = await AddInstrumentAsync("CT-DEL-INST");

        SqlException? exception = Assert.CatchAsync<SqlException>(
            () => ExecuteSqlAsync(
                "DELETE FROM WatchedInstruments WHERE Id = @id",
                instrumentId));

        int instruments;
        int rules;
        int revisions;
        await using (SqlConnection connection = new(_connectionString))
        {
            await connection.OpenAsync();
            instruments = await ScalarAsync(connection,
                "SELECT COUNT(*) FROM WatchedInstruments WHERE Id = @id", instrumentId);
            rules = await ScalarAsync(connection,
                "SELECT COUNT(*) FROM MonitoringRules WHERE WatchedInstrumentId = @id",
                instrumentId);
            revisions = await ScalarAsync(connection,
                "SELECT COUNT(*) FROM MonitoringRuleRevisions r " +
                "JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
                "WHERE m.WatchedInstrumentId = @id",
                instrumentId);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(547));
            Assert.That(instruments, Is.EqualTo(1));
            Assert.That(rules, Is.EqualTo(1));
            Assert.That(revisions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SqlConstraints_DeletingMonitoringRuleWithHistory_FailsAndPreservesRevisions()
    {
        Guid instrumentId = await AddInstrumentAsync("CT-DEL-RULE");

        SqlException? exception = Assert.CatchAsync<SqlException>(
            () => ExecuteSqlAsync(
                "DELETE FROM MonitoringRules WHERE WatchedInstrumentId = @id",
                instrumentId));

        int rules;
        int revisions;
        await using (SqlConnection connection = new(_connectionString))
        {
            await connection.OpenAsync();
            rules = await ScalarAsync(connection,
                "SELECT COUNT(*) FROM MonitoringRules WHERE WatchedInstrumentId = @id",
                instrumentId);
            revisions = await ScalarAsync(connection,
                "SELECT COUNT(*) FROM MonitoringRuleRevisions r " +
                "JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
                "WHERE m.WatchedInstrumentId = @id",
                instrumentId);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Number, Is.EqualTo(547));
            Assert.That(rules, Is.EqualTo(1));
            Assert.That(revisions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SqlConstraints_DeletingMonitoringRuleAfterRevisionsRemoved_Succeeds()
    {
        Guid instrumentId = await AddInstrumentAsync("CT-DEL-EMPTY");
        Guid ruleId = await ReadRuleIdAsync(instrumentId);

        await ExecuteSqlAsync(
            "DELETE FROM MonitoringRuleRevisions WHERE MonitoringRuleId = @id",
            ruleId);
        await ExecuteSqlAsync(
            "DELETE FROM MonitoringRules WHERE WatchedInstrumentId = @id",
            instrumentId);

        int rules;
        int instruments;
        await using (SqlConnection connection = new(_connectionString))
        {
            await connection.OpenAsync();
            rules = await ScalarAsync(connection,
                "SELECT COUNT(*) FROM MonitoringRules WHERE WatchedInstrumentId = @id",
                instrumentId);
            instruments = await ScalarAsync(connection,
                "SELECT COUNT(*) FROM WatchedInstruments WHERE Id = @id", instrumentId);
        }

        Assert.Multiple(() =>
        {
            Assert.That(rules, Is.EqualTo(0));
            Assert.That(instruments, Is.EqualTo(1));
        });
    }

    private async Task<Guid> SeedRuleAsync(string symbol)
    {
        Guid instrumentId = await AddInstrumentAsync(symbol);

        return await ReadRuleIdAsync(instrumentId);
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
            CreateDefinition());

        Result<Guid> added = await store.AddAsync(registration, CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);

        return added.Value;
    }

    private async Task<Guid> ReadRuleIdAsync(Guid instrumentId)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id FROM MonitoringRules WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> CountRevisionsAsync(Guid ruleId)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        return await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM MonitoringRuleRevisions WHERE MonitoringRuleId = @id",
            ruleId);
    }

    private static async Task<int> ScalarAsync(
        SqlConnection connection,
        string sql,
        Guid parameterValue)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", parameterValue);

        return (int)(await command.ExecuteScalarAsync())!;
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

    private async Task InsertRevisionAsync(
        Guid ruleId,
        Guid revisionId,
        int? revisionNumber,
        Instant? effectiveFrom,
        Instant? effectiveTo,
        Instant? proposedFrom,
        Instant? proposedTo)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO MonitoringRuleRevisions (Id, MonitoringRuleId, RevisionNumber, " +
            "CreatedAt, CreatedBy, ChangeReason, DefinitionXml, EffectiveFrom, EffectiveTo, " +
            "ProposedFrom, ProposedTo) " +
            "VALUES (@id, @ruleId, @number, @createdAt, N'synthetic-user', N'synthetic change', " +
            "@xml, @from, @to, @proposedFrom, @proposedTo)";
        command.Parameters.AddWithValue("@id", revisionId);
        command.Parameters.AddWithValue("@ruleId", ruleId);
        command.Parameters.AddWithValue("@createdAt", January2.ToDateTimeUtc());
        command.Parameters.AddWithValue("@xml", _serializer.Serialize(CreateDefinition()));
        command.Parameters.Add(new SqlParameter("@number", SqlDbType.Int)
        {
            Value = revisionNumber.HasValue ? revisionNumber.Value : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@from", SqlDbType.DateTime2)
        {
            Value = effectiveFrom.HasValue ? effectiveFrom.Value.ToDateTimeUtc() : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.DateTime2)
        {
            Value = effectiveTo.HasValue ? effectiveTo.Value.ToDateTimeUtc() : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@proposedFrom", SqlDbType.DateTime2)
        {
            Value = proposedFrom.HasValue ? proposedFrom.Value.ToDateTimeUtc() : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@proposedTo", SqlDbType.DateTime2)
        {
            Value = proposedTo.HasValue ? proposedTo.Value.ToDateTimeUtc() : DBNull.Value
        });
        await command.ExecuteNonQueryAsync();
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

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(_connectionString)
                .Options;

        return new TradingEngineDbContext(options);
    }
}
