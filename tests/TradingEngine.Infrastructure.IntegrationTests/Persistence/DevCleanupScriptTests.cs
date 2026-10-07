using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class DevCleanupScriptTests
{
    private MsSqlContainer _container = null!;
    private string _connectionString = null!;
    private string _databaseName = null!;
    private string _scriptTemplate = null!;
    private ChartAnalysisDefinitionXmlSerializer _serializer = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        _databaseName = new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
        _scriptTemplate = await File.ReadAllTextAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "DevCleanup.sql"));
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
    public async Task Apply_WhenRunRowsExist_RemovesRunRowsAndPreservesUnrelatedData()
    {
        Guid target = await SeedInstrumentAsync("RUN-T81A-DEMO", MonitoringState.Configured);
        Guid monitoredSameRun = await SeedInstrumentAsync("RUN-T81A-WATCH", MonitoringState.Monitored);
        Guid otherRun = await SeedInstrumentAsync("RUN-T81B-DEMO", MonitoringState.Configured);
        Guid sentinel = await SeedInstrumentAsync("KEEP-T81X", MonitoringState.Configured);
        await SeedDraftRevisionAsync(target);
        await SeedLegacyDefinitionAsync(target);

        await RunCleanupAsync("T81A", apply: true);

        Assert.Multiple(async () =>
        {
            Assert.That(await InstrumentCountAsync(target), Is.EqualTo(0));
            Assert.That(await RuleCountAsync(target), Is.EqualTo(0));
            Assert.That(await RevisionCountAsync(target), Is.EqualTo(0));
            Assert.That(await LegacyDefinitionCountAsync(target), Is.EqualTo(0));

            Assert.That(await InstrumentCountAsync(monitoredSameRun), Is.EqualTo(1));
            Assert.That(await InstrumentCountAsync(otherRun), Is.EqualTo(1));
            Assert.That(await InstrumentCountAsync(sentinel), Is.EqualTo(1));
            Assert.That(await RevisionCountAsync(sentinel), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Apply_WhenRunAlreadyCleaned_IsHarmlessNoOp()
    {
        Guid target = await SeedInstrumentAsync("RUN-T81C-DEMO", MonitoringState.Configured);

        await RunCleanupAsync("T81C", apply: true);
        await RunCleanupAsync("T81C", apply: true);

        Assert.That(await InstrumentCountAsync(target), Is.EqualTo(0));
    }

    [Test]
    public async Task Apply_WhenRunNeverExisted_IsHarmlessNoOp()
    {
        await RunCleanupAsync("T81NONE", apply: true);
    }

    [Test]
    public async Task Preview_WhenNotApplied_LeavesAllRows()
    {
        Guid target = await SeedInstrumentAsync("RUN-T81P-DEMO", MonitoringState.Configured);

        await RunCleanupAsync("T81P", apply: false);

        Assert.Multiple(async () =>
        {
            Assert.That(await InstrumentCountAsync(target), Is.EqualTo(1));
            Assert.That(await RuleCountAsync(target), Is.EqualTo(1));
            Assert.That(await RevisionCountAsync(target), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Apply_WhenDatabaseNameMismatch_FailsBeforeDeleting()
    {
        Guid target = await SeedInstrumentAsync("RUN-T81D-DEMO", MonitoringState.Configured);
        string script = BuildScript("T81D", "NotTheConnectedDatabase", apply: true);

        SqlException? exception = await Assert.ThrowsAsync<SqlException>(
            () => ExecuteScriptAsync(script));

        Assert.Multiple(async () =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(await InstrumentCountAsync(target), Is.EqualTo(1));
        });
    }

    private async Task<Guid> SeedInstrumentAsync(string symbol, MonitoringState monitoringState)
    {
        WatchedInstrumentFields fields = WatchedInstrument
            .Validate(symbol, "XTEST", "GBP", 60)
            .Value;
        WatchedInstrumentRegistration registration = new(
            fields,
            monitoringState,
            Instant.FromUtc(2026, 1, 2, 9, 30),
            CreateDefinition());

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        return (await store.AddAsync(registration, CancellationToken.None)).Value;
    }

    private async Task SeedDraftRevisionAsync(Guid instrumentId)
    {
        const string sql = """
            INSERT INTO MonitoringRuleRevisions
                (Id, MonitoringRuleId, RevisionNumber, CreatedAt, CreatedBy, ChangeReason,
                 DefinitionXml, EffectiveFrom, EffectiveTo, ProposedFrom, ProposedTo)
            SELECT NEWID(), m.Id, NULL, SYSUTCDATETIME(), N'dev-cleanup-test', N'draft row',
                   @DefinitionXml, NULL, NULL, NULL, NULL
            FROM MonitoringRules m
            WHERE m.WatchedInstrumentId = @InstrumentId
            """;

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@InstrumentId", instrumentId);
        command.Parameters.AddWithValue("@DefinitionXml", _serializer.Serialize(CreateDefinition()));
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedLegacyDefinitionAsync(Guid instrumentId)
    {
        const string sql = """
            INSERT INTO ChartAnalysisDefinitions (WatchedInstrumentId, DefinitionXml)
            VALUES (@InstrumentId, @DefinitionXml)
            """;

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@InstrumentId", instrumentId);
        command.Parameters.AddWithValue("@DefinitionXml", _serializer.Serialize(CreateDefinition()));
        await command.ExecuteNonQueryAsync();
    }

    private async Task RunCleanupAsync(string runId, bool apply)
    {
        await ExecuteScriptAsync(BuildScript(runId, _databaseName, apply));
    }

    private string BuildScript(string runId, string databaseName, bool apply)
    {
        return _scriptTemplate
            .Replace("$(RUN_ID)", runId)
            .Replace("$(EXPECTED_DATABASE)", databaseName)
            .Replace("$(APPLY)", apply ? "1" : "0");
    }

    private async Task ExecuteScriptAsync(string script)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(script, connection);
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ScalarCountAsync(string sql, Guid instrumentId)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@InstrumentId", instrumentId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> InstrumentCountAsync(Guid instrumentId)
    {
        return await ScalarCountAsync(
            "SELECT COUNT(*) FROM WatchedInstruments WHERE Id = @InstrumentId",
            instrumentId);
    }

    private async Task<int> RuleCountAsync(Guid instrumentId)
    {
        return await ScalarCountAsync(
            "SELECT COUNT(*) FROM MonitoringRules WHERE WatchedInstrumentId = @InstrumentId",
            instrumentId);
    }

    private async Task<int> RevisionCountAsync(Guid instrumentId)
    {
        return await ScalarCountAsync(
            """
            SELECT COUNT(*)
            FROM MonitoringRuleRevisions r
            JOIN MonitoringRules m ON m.Id = r.MonitoringRuleId
            WHERE m.WatchedInstrumentId = @InstrumentId
            """,
            instrumentId);
    }

    private async Task<int> LegacyDefinitionCountAsync(Guid instrumentId)
    {
        return await ScalarCountAsync(
            "SELECT COUNT(*) FROM ChartAnalysisDefinitions WHERE WatchedInstrumentId = @InstrumentId",
            instrumentId);
    }

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options = new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new TradingEngineDbContext(options);
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
