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
    private static readonly Guid UnknownInstrumentId = Guid.Parse("2f6f9c1a-3b7e-4d5a-9c8b-1a2b3c4d5e02");
    private static readonly Guid UnavailableDatabaseInstrumentId = Guid.Parse("2f6f9c1a-3b7e-4d5a-9c8b-1a2b3c4d5e08");

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
    public async Task AddAsync_ThenGetAsync_RoundTripsConfiguration()
    {
        WatchedInstrumentRegistration registration = CreateRegistration(
            "demo-2",
            "xtest",
            "gbp",
            MonitoringState.Monitored);

        Result<Guid> added;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            added = await store.AddAsync(registration, CancellationToken.None);
        }

        Assert.That(added.IsSuccess, Is.True);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            Result<WatchedInstrumentConfiguration> retrieved = await store.GetAsync(
                added.Value,
                CancellationToken.None);

            Assert.That(retrieved.IsSuccess, Is.True);
            WatchedInstrument restored = retrieved.Value.Instrument;
            Assert.Multiple(() =>
            {
                Assert.That(restored.Id, Is.EqualTo(added.Value));
                Assert.That(restored.Symbol, Is.EqualTo("DEMO-2"));
                Assert.That(restored.Exchange, Is.EqualTo("XTEST"));
                Assert.That(restored.QuoteCurrency, Is.EqualTo("GBP"));
                Assert.That(restored.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
                Assert.That(restored.SamplingIntervalSeconds, Is.EqualTo(60));
                Assert.That(restored.CreatedAt, Is.EqualTo(registration.CreatedAt));
                Assert.That(restored.LastChangedAt, Is.EqualTo(registration.CreatedAt));
                Assert.That(
                    _serializer.Serialize(retrieved.Value.Definition),
                    Is.EqualTo(_serializer.Serialize(registration.Definition)));
            });
        }
    }

    [Test]
    public async Task AddAsync_AssignsSameGeneratedIdToBothRows()
    {
        Result<Guid> added;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            added = await store.AddAsync(
                CreateRegistration("AAA", "XTEST", "USD", MonitoringState.Configured),
                CancellationToken.None);
        }

        Assert.That(added.IsSuccess, Is.True);
        Assert.That(added.Value, Is.Not.EqualTo(Guid.Empty));

        int instrumentRows;
        int definitionRows;
        await using (TradingEngineDbContext context = CreateContext())
        {
            instrumentRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM WatchedInstruments WHERE Id = {added.Value}")
                .SingleAsync();
            definitionRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM ChartAnalysisDefinitions WHERE WatchedInstrumentId = {added.Value}")
                .SingleAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(instrumentRows, Is.EqualTo(1));
            Assert.That(definitionRows, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddAsync_ConsecutiveAdds_GenerateSqlServerOrderedIds()
    {
        Result<Guid> first;
        Result<Guid> second;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            first = await store.AddAsync(
                CreateRegistration("SEQ-A", "XTEST", "USD", MonitoringState.Configured),
                CancellationToken.None);
            second = await store.AddAsync(
                CreateRegistration("SEQ-B", "XTEST", "USD", MonitoringState.Configured),
                CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(second.Value, Is.Not.EqualTo(first.Value));
        });

        List<Guid> ordered = new();
        await using (SqlConnection connection = new(_connectionString))
        {
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT [Value] FROM (VALUES (@first), (@second)) AS ids([Value]) ORDER BY [Value]";
            command.Parameters.AddWithValue("@first", first.Value);
            command.Parameters.AddWithValue("@second", second.Value);

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ordered.Add(reader.GetGuid(0));
            }
        }

        Assert.That(ordered, Is.EqualTo(new[] { first.Value, second.Value }));
    }

    [Test]
    public async Task GetAsync_WithUnknownId_ReturnsNotFound()
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        Result<WatchedInstrumentConfiguration> result = await store.GetAsync(
            UnknownInstrumentId,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ConfigurationNotFound));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        });
    }

    [Test]
    public async Task AddAsync_WithDuplicateBusinessKey_ReturnsConflictAndLeavesNoPartialRow()
    {
        Result<Guid> firstAdd;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            firstAdd = await store.AddAsync(
                CreateRegistration("CCC", "XTEST", "EUR", MonitoringState.Configured),
                CancellationToken.None);
        }

        Result<Guid> secondAdd;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            secondAdd = await store.AddAsync(
                CreateRegistration("CCC", "XTEST", "EUR", MonitoringState.Configured),
                CancellationToken.None);
        }

        int instrumentRows;
        int orphanedDefinitionRows;
        await using (TradingEngineDbContext context = CreateContext())
        {
            instrumentRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM WatchedInstruments WHERE Exchange = {"XTEST"} AND Symbol = {"CCC"} AND QuoteCurrency = {"EUR"}")
                .SingleAsync();
            orphanedDefinitionRows = await context.Database
                .SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM ChartAnalysisDefinitions d WHERE NOT EXISTS (SELECT 1 FROM WatchedInstruments w WHERE w.Id = d.WatchedInstrumentId)")
                .SingleAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(firstAdd.IsSuccess, Is.True);
            Assert.That(secondAdd.IsFailure, Is.True);
            Assert.That(secondAdd.Error, Is.EqualTo(WatchedInstrumentErrors.DuplicateBusinessKey));
            Assert.That(secondAdd.Error.Type, Is.EqualTo(ErrorType.Conflict));
            Assert.That(instrumentRows, Is.EqualTo(1));
            Assert.That(orphanedDefinitionRows, Is.EqualTo(0));
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
        WatchedInstrumentRegistration registration = CreateRegistration(
            "DDD",
            "XTEST",
            "JPY",
            MonitoringState.Configured);

        Result<Guid> added;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore store = new(context, _serializer);
            added = await store.AddAsync(registration, CancellationToken.None);
        }

        Assert.That(added.IsSuccess, Is.True);

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT CONVERT(nvarchar(max), DefinitionXml) FROM ChartAnalysisDefinitions " +
            "WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", added.Value);

        string? storedXml = (string?)await command.ExecuteScalarAsync();

        Assert.That(storedXml, Is.Not.Null);
        Assert.That(
            XElement.Parse(storedXml!).ToString(),
            Is.EqualTo(XElement.Parse(_serializer.Serialize(registration.Definition)).ToString()));
    }

    [Test]
    public async Task AddAsync_WhenCancelled_PropagatesCancellation()
    {
        WatchedInstrumentRegistration registration = CreateRegistration(
            "EEE",
            "XTEST",
            "CHF",
            MonitoringState.Configured);

        await using TradingEngineDbContext context = CreateContext();
        SqlServerWatchedInstrumentStore store = new(context, _serializer);
        CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Assert.CatchAsync<OperationCanceledException>(
            () => store.AddAsync(registration, cancellation.Token));
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

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetAsync(UnavailableDatabaseInstrumentId, CancellationToken.None));

        Assert.That(exception?.InnerException, Is.TypeOf<SqlException>());
    }

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options = new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new TradingEngineDbContext(options);
    }

    private static WatchedInstrumentRegistration CreateRegistration(
        string symbol,
        string exchange,
        string quoteCurrency,
        MonitoringState monitoringState)
    {
        WatchedInstrumentFields fields = WatchedInstrument
            .Validate(symbol, exchange, quoteCurrency, 60)
            .Value;

        return new WatchedInstrumentRegistration(
            fields,
            monitoringState,
            Instant.FromUtc(2026, 1, 2, 9, 30),
            CreateDefinition());
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
