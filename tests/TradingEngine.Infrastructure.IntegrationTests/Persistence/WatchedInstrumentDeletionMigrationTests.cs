using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.MsSql;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class WatchedInstrumentDeletionMigrationTests
{
    private const string Database = "DeletionUpgrade";
    private const string PreviousMigration = "20261014192824_WatchedInstrumentSettingsConcurrency";
    private static readonly Guid InstrumentId =
        Guid.Parse("f1000000-0000-0000-0000-000000000001");

    private MsSqlContainer _container = null!;
    private string _connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task Upgrade_ToLogicalDeletion_KeepsRowsAndEnforcesActiveOnlyUniqueness()
    {
        await using (TradingEngineDbContext context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using (SqlConnection connection = new(ConnectionString()))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO WatchedInstruments " +
                "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, " +
                "SamplingIntervalSeconds, CreatedAt, LastChangedAt) " +
                "VALUES (@id, 'LEGACY-2', 'XTEST', 'USD', 'Monitored', 60, " +
                "'2026-01-02T09:30:00.0000000', '2026-01-02T09:30:00.0000000')";
            command.Parameters.AddWithValue("@id", InstrumentId);
            await command.ExecuteNonQueryAsync();
        }

        Guid ruleId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        await using (SqlConnection connection = new(ConnectionString()))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO MonitoringRules (Id, WatchedInstrumentId, CreatedAt) " +
                "VALUES (@ruleId, @instrumentId, '2026-01-02T09:30:00.0000000'); " +
                "INSERT INTO MonitoringRuleRevisions " +
                "(Id, MonitoringRuleId, RevisionNumber, CreatedAt, CreatedBy, " +
                "ChangeReason, DefinitionXml, EffectiveFrom, EffectiveTo, " +
                "ProposedFrom, ProposedTo) " +
                "VALUES (@revisionId, @ruleId, 1, '2026-01-02T09:30:00.0000000', " +
                "'synthetic-user', 'legacy revision', '<definition scale=\"4\" />', " +
                "'2026-01-02T10:00:00.0000000', NULL, NULL, NULL)";
            command.Parameters.AddWithValue("@ruleId", ruleId);
            command.Parameters.AddWithValue("@instrumentId", InstrumentId);
            command.Parameters.AddWithValue("@revisionId", revisionId);
            await command.ExecuteNonQueryAsync();
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await using SqlConnection verify = new(ConnectionString());
        await verify.OpenAsync();

        await using (SqlCommand existing = verify.CreateCommand())
        {
            existing.CommandText =
                "SELECT DeletedAt, Symbol, MonitoringState " +
                "FROM WatchedInstruments WHERE Id = @id";
            existing.Parameters.AddWithValue("@id", InstrumentId);
            await using SqlDataReader reader = await existing.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.IsDBNull(0), Is.True);
                Assert.That(reader.GetString(1), Is.EqualTo("LEGACY-2"));
                Assert.That(reader.GetString(2), Is.EqualTo("Monitored"));
            });
        }

        await using (SqlCommand duplicateActive = verify.CreateCommand())
        {
            duplicateActive.CommandText =
                "INSERT INTO WatchedInstruments " +
                "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, " +
                "SamplingIntervalSeconds, CreatedAt, LastChangedAt) " +
                "VALUES (@id, 'LEGACY-2', 'XTEST', 'USD', 'Configured', 60, " +
                "'2026-01-03T09:30:00.0000000', '2026-01-03T09:30:00.0000000')";
            duplicateActive.Parameters.AddWithValue("@id", Guid.NewGuid());
            SqlException? violation = Assert.Throws<SqlException>(
                () => duplicateActive.ExecuteNonQueryAsync().GetAwaiter().GetResult());
            Assert.That(violation!.Message, Does.Contain("UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency"));
        }

        await using (SqlCommand markDeleted = verify.CreateCommand())
        {
            markDeleted.CommandText =
                "UPDATE WatchedInstruments SET DeletedAt = '2026-10-05T09:30:00.0000000' " +
                "WHERE Id = @id";
            markDeleted.Parameters.AddWithValue("@id", InstrumentId);
            await markDeleted.ExecuteNonQueryAsync();
        }

        await using (SqlCommand reRegister = verify.CreateCommand())
        {
            reRegister.CommandText =
                "INSERT INTO WatchedInstruments " +
                "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, " +
                "SamplingIntervalSeconds, CreatedAt, LastChangedAt) " +
                "VALUES (@id, 'LEGACY-2', 'XTEST', 'USD', 'Configured', 60, " +
                "'2026-01-03T09:30:00.0000000', '2026-01-03T09:30:00.0000000')";
            reRegister.Parameters.AddWithValue("@id", Guid.NewGuid());
            Assert.That(await reRegister.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        await using (SqlCommand history = verify.CreateCommand())
        {
            history.CommandText =
                "SELECT r.RevisionNumber, r.ChangeReason, r.DefinitionXml " +
                "FROM MonitoringRuleRevisions r " +
                "JOIN MonitoringRules m ON m.Id = r.MonitoringRuleId " +
                "WHERE m.WatchedInstrumentId = @id";
            history.Parameters.AddWithValue("@id", InstrumentId);
            await using SqlDataReader reader = await history.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));
                Assert.That(reader.GetString(1), Is.EqualTo("legacy revision"));
                Assert.That(reader.GetString(2), Does.Contain("definition"));
            });
        }
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
}
