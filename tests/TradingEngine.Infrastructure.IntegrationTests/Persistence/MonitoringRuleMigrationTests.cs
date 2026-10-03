using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class MonitoringRuleMigrationTests
{
    private const string LegacyInstrumentId = "9e8d7c6b-5a4f-4e3d-8c2b-1a0b9c8d7e6f";
    private const string InitialMigration = "20260918172032_InitialCurrentConfiguration";

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
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task Upgrade_FromLegacySchema_BackfillsInstrumentAndInitialRevision()
    {
        string legacyXml = _serializer.Serialize(CreateDefinition());

        await using (TradingEngineDbContext context = CreateContext("UpgradeA"))
        {
            await context.GetService<IMigrator>().MigrateAsync(InitialMigration);
        }

        await SeedLegacyInstrumentAsync("UpgradeA", legacyXml);

        Instant cutoverBefore;
        Instant cutoverAfter;
        await using (SqlConnection connection = new(ConnectionStringFor("UpgradeA")))
        {
            await connection.OpenAsync();
            cutoverBefore = ToInstant(await UtcNowAsync(connection));
        }

        await using (TradingEngineDbContext context = CreateContext("UpgradeA"))
        {
            await context.Database.MigrateAsync();
        }

        await using (SqlConnection connection = new(ConnectionStringFor("UpgradeA")))
        {
            await connection.OpenAsync();
            cutoverAfter = ToInstant(await UtcNowAsync(connection));
        }

        await using (TradingEngineDbContext context = CreateContext("UpgradeA"))
        {
            int ruleRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM MonitoringRules WHERE WatchedInstrumentId = {Guid.Parse(LegacyInstrumentId)}")
                .SingleAsync();
            int legacyRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM ChartAnalysisDefinitions WHERE WatchedInstrumentId = {Guid.Parse(LegacyInstrumentId)}")
                .SingleAsync();

            Assert.Multiple(() =>
            {
                Assert.That(ruleRows, Is.EqualTo(1));
                Assert.That(legacyRows, Is.EqualTo(1));
            });
        }

        await using (SqlConnection connection = new(ConnectionStringFor("UpgradeA")))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT r.RevisionNumber, r.CreatedAt, r.CreatedBy, " +
                "CONVERT(nvarchar(max), r.DefinitionXml), r.EffectiveFrom, r.EffectiveTo " +
                "FROM MonitoringRuleRevisions r " +
                "JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
                "WHERE m.WatchedInstrumentId = @id";
            command.Parameters.AddWithValue("@id", Guid.Parse(LegacyInstrumentId));

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);

            int number = reader.GetInt32(0);
            DateTime createdAt = reader.GetDateTime(1);
            string createdBy = reader.GetString(2);
            string storedXml = reader.GetString(3);
            DateTime effectiveFrom = reader.GetDateTime(4);
            object effectiveTo = reader.GetValue(5);

            Assert.Multiple(() =>
            {
                Assert.That(number, Is.EqualTo(1));
                Assert.That(createdBy, Is.EqualTo("migration-20261003150626"));
                Assert.That(
                    System.Xml.Linq.XElement.Parse(storedXml).ToString(),
                    Is.EqualTo(System.Xml.Linq.XElement.Parse(legacyXml).ToString()));
                Assert.That(ToInstant(createdAt), Is.GreaterThanOrEqualTo(cutoverBefore));
                Assert.That(ToInstant(createdAt), Is.LessThanOrEqualTo(cutoverAfter));
                Assert.That(ToInstant(effectiveFrom), Is.GreaterThanOrEqualTo(cutoverBefore));
                Assert.That(ToInstant(effectiveFrom), Is.LessThanOrEqualTo(cutoverAfter));
                Assert.That(effectiveTo, Is.EqualTo(DBNull.Value));
            });
        }
    }

    [Test]
    public async Task Upgrade_ThenRead_ResolvesDefinitionAtAndAfterCutoverOnly()
    {
        string legacyXml = _serializer.Serialize(CreateDefinition());

        await using (TradingEngineDbContext context = CreateContext("UpgradeB"))
        {
            await context.GetService<IMigrator>().MigrateAsync(InitialMigration);
        }

        await SeedLegacyInstrumentAsync("UpgradeB", legacyXml);

        await using (TradingEngineDbContext context = CreateContext("UpgradeB"))
        {
            await context.Database.MigrateAsync();
        }

        DateTime cutover;
        await using (SqlConnection connection = new(ConnectionStringFor("UpgradeB")))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT r.EffectiveFrom FROM MonitoringRuleRevisions r " +
                "JOIN MonitoringRules m ON r.MonitoringRuleId = m.Id " +
                "WHERE m.WatchedInstrumentId = @id";
            command.Parameters.AddWithValue("@id", Guid.Parse(LegacyInstrumentId));
            cutover = (DateTime)(await command.ExecuteScalarAsync())!;
        }

        Instant atCutover = ToInstant(cutover);

        await using (TradingEngineDbContext context = CreateContext("UpgradeB"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);

            Result<WatchedInstrumentConfiguration> at = await store.GetAsync(
                Guid.Parse(LegacyInstrumentId),
                atCutover,
                CancellationToken.None);
            Result<WatchedInstrumentConfiguration> after = await store.GetAsync(
                Guid.Parse(LegacyInstrumentId),
                atCutover + Duration.FromTicks(1),
                CancellationToken.None);
            Result<WatchedInstrumentConfiguration> before = await store.GetAsync(
                Guid.Parse(LegacyInstrumentId),
                atCutover - Duration.FromTicks(1),
                CancellationToken.None);
            Result<MonitoringRuleSnapshot> rule = await new SqlServerMonitoringRuleStore(
                    context,
                    _serializer)
                .GetAsync(Guid.Parse(LegacyInstrumentId), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(at.IsSuccess, Is.True);
                Assert.That(
                    _serializer.Serialize(at.Value.Definition),
                    Is.EqualTo(legacyXml));
                Assert.That(after.IsSuccess, Is.True);
                Assert.That(before.IsFailure, Is.True);
                Assert.That(before.Error, Is.EqualTo(MonitoringRuleErrors.NoApplicableRevision));
                Assert.That(rule.IsSuccess, Is.True);
                Assert.That(rule.Value.Rule.Revisions, Has.Count.EqualTo(1));
            });
        }
    }

    private async Task SeedLegacyInstrumentAsync(string database, string definitionXml)
    {
        await using SqlConnection connection = new(ConnectionStringFor(database));
        await connection.OpenAsync();

        await using SqlCommand instrument = connection.CreateCommand();
        instrument.CommandText =
            "INSERT INTO WatchedInstruments " +
            "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, " +
            "CreatedAt, LastChangedAt) " +
            "VALUES (@id, N'LEG-1', N'XTEST', N'USD', N'Configured', 60, " +
            "CONVERT(datetime2(7), '2026-01-02T09:30:00'), " +
            "CONVERT(datetime2(7), '2026-01-02T09:30:00'))";
        instrument.Parameters.AddWithValue("@id", Guid.Parse(LegacyInstrumentId));
        await instrument.ExecuteNonQueryAsync();

        await using SqlCommand definition = connection.CreateCommand();
        definition.CommandText =
            "INSERT INTO ChartAnalysisDefinitions (WatchedInstrumentId, DefinitionXml) " +
            "VALUES (@id, @xml)";
        definition.Parameters.AddWithValue("@id", Guid.Parse(LegacyInstrumentId));
        definition.Parameters.AddWithValue("@xml", definitionXml);
        await definition.ExecuteNonQueryAsync();
    }

    private static Instant ToInstant(DateTime value)
    {
        return Instant.FromDateTimeUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static async Task<DateTime> UtcNowAsync(SqlConnection connection)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT SYSUTCDATETIME()";

        return (DateTime)(await command.ExecuteScalarAsync())!;
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

    private string ConnectionStringFor(string database)
    {
        return new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = database
        }.ConnectionString;
    }

    private TradingEngineDbContext CreateContext(string database)
    {
        DbContextOptions<TradingEngineDbContext> options = new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(ConnectionStringFor(database))
            .Options;

        return new TradingEngineDbContext(options);
    }
}
