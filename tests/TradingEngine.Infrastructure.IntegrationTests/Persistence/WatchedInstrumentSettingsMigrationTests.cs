using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class WatchedInstrumentSettingsMigrationTests
{
    private const string PreviousMigration = "20261003150626_MonitoringRuleRevisionHistory";
    private static readonly Guid InstrumentId =
        Guid.Parse("5c6d7e8f-9a0b-4c1d-9e2f-3a4b5c6d7e8f");

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
    public async Task Upgrade_ToSettingsConcurrency_KeepsExistingRowsAndAssignsRowVersion()
    {
        await using (TradingEngineDbContext context = CreateContext("SettingsUpgrade"))
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using (SqlConnection connection = new(ConnectionStringFor("SettingsUpgrade")))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO WatchedInstruments " +
                "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, " +
                "SamplingIntervalSeconds, CreatedAt, LastChangedAt) " +
                "VALUES (@id, 'LEGACY-1', 'XTEST', 'USD', 'Monitored', 60, " +
                "'2026-01-02T09:30:00.0000000', '2026-01-02T09:30:00.0000000')";
            command.Parameters.AddWithValue("@id", InstrumentId);
            await command.ExecuteNonQueryAsync();
        }

        await using (TradingEngineDbContext context = CreateContext("SettingsUpgrade"))
        {
            await context.Database.MigrateAsync();
        }

        await using (SqlConnection connection = new(ConnectionStringFor("SettingsUpgrade")))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT Symbol, MonitoringState, SamplingIntervalSeconds, RowVersion " +
                "FROM WatchedInstruments WHERE Id = @id";
            command.Parameters.AddWithValue("@id", InstrumentId);
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            string symbol = reader.GetString(0);
            string state = reader.GetString(1);
            int interval = reader.GetInt32(2);
            byte[] rowVersion = (byte[])reader.GetValue(3);

            Assert.Multiple(() =>
            {
                Assert.That(symbol, Is.EqualTo("LEGACY-1"));
                Assert.That(state, Is.EqualTo("Monitored"));
                Assert.That(interval, Is.EqualTo(60));
                Assert.That(rowVersion, Is.Not.Empty);
            });
        }

        await using (TradingEngineDbContext context = CreateContext("SettingsUpgrade"))
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentSettingsSnapshot> snapshot =
                await store.GetSettingsAsync(InstrumentId, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.IsSuccess, Is.True);
                Assert.That(snapshot.Value.ConcurrencyToken, Is.Not.Empty);
                Assert.That(
                    snapshot.Value.Instrument.MonitoringState,
                    Is.EqualTo(MonitoringState.Monitored));
            });
        }
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
}
