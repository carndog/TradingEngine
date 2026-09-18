using System.Xml.Linq;
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
public sealed class CurrentConfigurationPersistenceTests
{
    private MsSqlContainer _container = null!;
    private string _connectionString = null!;
    private ChartAnalysisDefinitionXmlSerializer _serializer = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
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
    public async Task AddAsync_ThenGetAsync_RoundTripsConfiguration()
    {
        Instant createdAt = Instant.FromUtc(2026, 1, 2, 9, 30).PlusNanoseconds(123456700);
        Instant changedAt = createdAt.PlusNanoseconds(765432100);
        WatchedInstrument instrument = WatchedInstrument
            .Create(Guid.NewGuid(), "demo-2", "xtest", "gbp", 60, createdAt)
            .Value;
        Assert.That(instrument.StartMonitoring(120, changedAt).IsSuccess, Is.True);
        ChartAnalysisDefinition definition = CreateDefinition();

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result added = await store.AddAsync(
                new WatchedInstrumentConfiguration(instrument, definition),
                CancellationToken.None);
            Assert.That(added.IsSuccess, Is.True);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentConfiguration> retrieved = await store.GetAsync(
                instrument.Id,
                CancellationToken.None);

            Assert.That(retrieved.IsSuccess, Is.True);
            WatchedInstrument restored = retrieved.Value.Instrument;
            Assert.Multiple(() =>
            {
                Assert.That(restored.Id, Is.EqualTo(instrument.Id));
                Assert.That(restored.Symbol, Is.EqualTo("DEMO-2"));
                Assert.That(restored.Exchange, Is.EqualTo("XTEST"));
                Assert.That(restored.QuoteCurrency, Is.EqualTo("GBP"));
                Assert.That(restored.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
                Assert.That(restored.SamplingIntervalSeconds, Is.EqualTo(120));
                Assert.That(restored.CreatedAt, Is.EqualTo(createdAt));
                Assert.That(restored.LastChangedAt, Is.EqualTo(changedAt));
                Assert.That(
                    _serializer.Serialize(retrieved.Value.Definition),
                    Is.EqualTo(_serializer.Serialize(definition)));
            });
        }
    }

    [Test]
    public async Task GetAsync_WithUnknownId_ReturnsNotFound()
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        Result<WatchedInstrumentConfiguration> result = await store.GetAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ConfigurationNotFound));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        });
    }

    [Test]
    public async Task AddAsync_WithDuplicateId_ReturnsConflict()
    {
        Guid id = Guid.NewGuid();
        WatchedInstrument first = CreateInstrument(id, "AAA", "XTEST", "USD");
        WatchedInstrument second = CreateInstrument(id, "BBB", "XTEST", "USD");

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        Result firstAdd = await store.AddAsync(
            new WatchedInstrumentConfiguration(first, CreateDefinition()),
            CancellationToken.None);
        Result secondAdd = await store.AddAsync(
            new WatchedInstrumentConfiguration(second, CreateDefinition()),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstAdd.IsSuccess, Is.True);
            Assert.That(secondAdd.IsFailure, Is.True);
            Assert.That(secondAdd.Error, Is.EqualTo(WatchedInstrumentErrors.DuplicateId));
            Assert.That(secondAdd.Error.Type, Is.EqualTo(ErrorType.Conflict));
        });
    }

    [Test]
    public async Task AddAsync_WithDuplicateBusinessKey_ReturnsConflictAndLeavesNoPartialRow()
    {
        WatchedInstrument first = CreateInstrument(Guid.NewGuid(), "CCC", "XTEST", "EUR");
        WatchedInstrument second = CreateInstrument(Guid.NewGuid(), "CCC", "XTEST", "EUR");

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        Result firstAdd = await store.AddAsync(
            new WatchedInstrumentConfiguration(first, CreateDefinition()),
            CancellationToken.None);
        Result secondAdd = await store.AddAsync(
            new WatchedInstrumentConfiguration(second, CreateDefinition()),
            CancellationToken.None);
        Result<WatchedInstrumentConfiguration> orphaned = await store.GetAsync(
            second.Id,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstAdd.IsSuccess, Is.True);
            Assert.That(secondAdd.IsFailure, Is.True);
            Assert.That(secondAdd.Error, Is.EqualTo(WatchedInstrumentErrors.DuplicateBusinessKey));
            Assert.That(secondAdd.Error.Type, Is.EqualTo(ErrorType.Conflict));
            Assert.That(orphaned.IsFailure, Is.True);
            Assert.That(orphaned.Error, Is.EqualTo(WatchedInstrumentErrors.ConfigurationNotFound));
        });
    }

    [Test]
    public async Task Schema_DefinitionXmlColumn_IsSqlServerXmlType()
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_NAME = 'ChartAnalysisDefinitions' AND COLUMN_NAME = 'DefinitionXml'";

        object? dataType = await command.ExecuteScalarAsync();

        Assert.That(dataType, Is.EqualTo("xml"));
    }

    [Test]
    public async Task AddAsync_PersistsCanonicalXmlInDefinitionColumn()
    {
        WatchedInstrument instrument = CreateInstrument(Guid.NewGuid(), "DDD", "XTEST", "JPY");
        ChartAnalysisDefinition definition = CreateDefinition();

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        await store.AddAsync(
            new WatchedInstrumentConfiguration(instrument, definition),
            CancellationToken.None);

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONVERT(nvarchar(max), DefinitionXml) FROM ChartAnalysisDefinitions " +
            "WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrument.Id);

        string? storedXml = (string?)await command.ExecuteScalarAsync();

        Assert.That(storedXml, Is.Not.Null);
        Assert.That(
            XElement.Parse(storedXml!).ToString(),
            Is.EqualTo(XElement.Parse(_serializer.Serialize(definition)).ToString()));
    }

    [Test]
    public async Task AddAsync_WhenCancelled_PropagatesCancellation()
    {
        WatchedInstrument instrument = CreateInstrument(Guid.NewGuid(), "EEE", "XTEST", "CHF");

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => store.AddAsync(
                new WatchedInstrumentConfiguration(instrument, CreateDefinition()),
                cancellation.Token));
    }

    [Test]
    public async Task GetAsync_WhenDatabaseUnavailable_Throws()
    {
        SqlConnectionStringBuilder builder = new(_connectionString)
        {
            InitialCatalog = "TradingEngineMissing",
            ConnectTimeout = 5
        };
        DbContextOptions<TradingEngineDbContext> options = new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        await using TradingEngineDbContext context = new(options);
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        Assert.ThrowsAsync<SqlException>(
            () => store.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options = new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new TradingEngineDbContext(options);
    }

    private static WatchedInstrument CreateInstrument(
        Guid id,
        string symbol,
        string exchange,
        string quoteCurrency)
    {
        return WatchedInstrument
            .Create(id, symbol, exchange, quoteCurrency, 60, Instant.FromUtc(2026, 1, 2, 9, 30))
            .Value;
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
