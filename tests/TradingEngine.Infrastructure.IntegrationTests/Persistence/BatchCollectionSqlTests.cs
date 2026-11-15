using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Infrastructure.MarketData;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class BatchCollectionSqlTests
{
    private MsSqlContainer _container = null!;
    private string _connectionString = null!;
    private readonly BatchTestClock _clock = new();
    private readonly BatchSqlCommandCounter _commands = new();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString() + ";Database=BatchCollection";
        await using TradingEngineDbContext context = Context();
        await context.Database.MigrateAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        _clock.Now = Instant.FromUtc(2026, 1, 5, 12, 0);
        await ExecuteAsync("DELETE FROM PriceCollectionAttempts; DELETE FROM PriceObservations; " +
            "DELETE FROM WatchedInstruments; DELETE FROM ListingConfigurations;");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _container.DisposeAsync();

    [Test]
    public async Task HandleAsync_OneSuccessAuditRejected_PreservesOtherResultsAndRecoversAfterRestart()
    {
        await SeedAsync(3);
        await ExecuteAsync("CREATE TRIGGER TR_Batch_Reject ON PriceCollectionAttempts AFTER INSERT AS " +
            "BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Outcome = 'Observed' AND " +
            "WatchedInstrumentId = '00000000-0000-0000-0000-000000000002') " +
            "THROW 51001, 'synthetic audit failure', 1; END");
        PriceCollectionRunResult first;
        try
        {
            using ServiceProvider provider = Provider(1000);
            first = await CollectAsync(provider);
            Assert.Multiple(() =>
            {
                Assert.That(first.Diagnostics.Persisted, Is.EqualTo(2));
                Assert.That(first.Diagnostics.Requests.RatesRequests, Is.EqualTo(1));
                Assert.That(first.Items.Count(item => item.ErrorCode == PriceCollectionErrors.StorageError.Code), Is.EqualTo(1));
            });
            Assert.That(await CountAsync("PriceObservations"), Is.EqualTo(2));
            Assert.That(await CountAsync("PriceCollectionAttempts", "Outcome = 'Observed'"), Is.EqualTo(2));
            Assert.That(await CountAsync("PriceCollectionAttempts", "Outcome = 'Failed'"), Is.EqualTo(1));
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER TR_Batch_Reject");
        }

        using ServiceProvider restarted = Provider(1000);
        PriceCollectionRunResult recovery = await CollectAsync(restarted);
        PriceCollectionRunResult replay = await CollectAsync(restarted);
        _clock.Now = _clock.Now.Plus(Duration.FromSeconds(900));
        PriceCollectionRunResult nextSlot = await CollectAsync(restarted);

        Assert.Multiple(() =>
        {
            Assert.That(recovery.Diagnostics.Persisted, Is.EqualTo(1));
            Assert.That(recovery.Diagnostics.Requests.SearchRequests, Is.Zero);
            Assert.That(replay.Diagnostics.Requests.RatesRequests, Is.Zero);
            Assert.That(nextSlot.Diagnostics.Persisted, Is.EqualTo(3));
        });
        Assert.That(await CountAsync("PriceObservations"), Is.EqualTo(6));
        Assert.That(await CountAsync("PriceCollectionAttempts", "Outcome = 'Observed'"), Is.EqualTo(6));
        Assert.That(await CountAsync("PriceCollectionAttempts", "Outcome = 'Failed'"), Is.EqualTo(1));
        Assert.That(await CountAsync("PriceObservations", "Bid = 12.345 AND Ask = 12.346"), Is.EqualTo(6));
    }

    [Test]
    public async Task HandleAsync_ConcurrentBatches_PreservesSingleObservationAndSuccessAuditPerSlot()
    {
        await SeedAsync(3);
        TaskCompletionSource bothRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requested = 0;
        using ServiceProvider provider = Provider(1000, async () =>
        {
            if (Interlocked.Increment(ref requested) == 2)
            {
                bothRequested.SetResult();
            }

            await bothRequested.Task.WaitAsync(TimeSpan.FromSeconds(20));
        });

        PriceCollectionRunResult[] runs = await Task.WhenAll(CollectAsync(provider), CollectAsync(provider));

        Assert.Multiple(() =>
        {
            Assert.That(runs.Sum(run => run.Diagnostics.Persisted), Is.EqualTo(3));
            Assert.That(runs.SelectMany(run => run.Items).Count(item => item.Outcome == PriceCollectionOutcome.SkippedDuplicate), Is.EqualTo(3));
        });
        Assert.That(await CountAsync("PriceObservations"), Is.EqualTo(3));
        Assert.That(await CountAsync("PriceCollectionAttempts", "Outcome = 'Observed'"), Is.EqualTo(3));
    }

    [TestCase(120, 1, 120)]
    [TestCase(120, 1000, 120)]
    [TestCase(1000, 1, 120)]
    [TestCase(1000, 1000, 1000)]
    public async Task HandleAsync_SyntheticSqlComparison_ReportsRequestsDatabaseWorkAndCoverage(
        int count, int batchSize, int expectedPersisted)
    {
        await SeedAsync(count);
        using ServiceProvider provider = Provider(batchSize);
        long before = _commands.Count;
        Stopwatch stopwatch = Stopwatch.StartNew();

        PriceCollectionRunResult result = await CollectAsync(provider);

        stopwatch.Stop();
        PriceCollectionRunDiagnostics diagnostics = result.Diagnostics;
        TestContext.Progress.WriteLine(
            $"BATCH_SQL_EVIDENCE instruments={count} batchSize={batchSize} " +
            $"rates={diagnostics.Requests.RatesRequests} searches={diagnostics.Requests.SearchRequests} " +
            $"sqlCommands={_commands.Count - before} elapsedMs={stopwatch.ElapsedMilliseconds} " +
            $"persisted={diagnostics.Persisted} deferred={diagnostics.Deferred} " +
            $"unprocessed={diagnostics.Unprocessed} deadline={result.DeadlineReached}");
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Persisted, Is.EqualTo(expectedPersisted));
            Assert.That(diagnostics.Requests.RatesRequests, Is.EqualTo(batchSize == 1 ? 120 : 1));
            Assert.That(diagnostics.Requests.SearchRequests, Is.Zero);
            Assert.That(diagnostics.Deferred, Is.EqualTo(count - expectedPersisted));
            Assert.That(diagnostics.Unprocessed, Is.Zero);
            Assert.That(result.DeadlineReached, Is.False);
        });
        Assert.That(await CountAsync("PriceObservations"), Is.EqualTo(expectedPersisted));
    }

    private ServiceProvider Provider(int batchSize, Func<Task>? beforeResponse = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EToro:MarketData:BaseUrl"] = "https://synthetic.invalid",
            ["EToro:MarketData:ApiKey"] = "synthetic-api-key",
            ["EToro:MarketData:UserKey"] = "synthetic-user-key",
            ["EToro:MarketData:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["EToro:MarketData:ExchangeMap:XTEST:0"] = "Test Exchange"
        }).Build();
        ServiceCollection services = new();
        services.AddSingleton<IClock>(_clock);
        services.AddTradingEngineInfrastructure(_connectionString);
        services.AddDbContext<TradingEngineDbContext>(options => options.AddInterceptors(_commands));
        services.AddEtoroMarketData(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(
            () => new SyntheticRatesHandler { BeforeResponse = beforeResponse }));
        services.AddSingleton<ISamplingPolicy, PerInstrumentSamplingPolicy>();
        services.AddSingleton<IMarketSessionPolicy>(new WeeklyScheduleMarketSessionPolicy(
            new Dictionary<string, ExchangeSession> { ["XTEST"] = new(null, true, []) }));
        services.AddSingleton(new PriceCollectionOptions { MaxExecutionTimeSeconds = 120 });
        services.AddSingleton<PriceCollectionProgress>();
        services.AddScoped<CollectDuePricesHandler>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<PriceCollectionRunResult> CollectAsync(ServiceProvider provider)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CollectDuePricesHandler>()
            .HandleAsync(new CollectDuePrices(), CancellationToken.None);
    }

    private async Task SeedAsync(int count)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        for (int index = 1; index <= count; index++)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO WatchedInstruments " +
                "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, SamplingIntervalSeconds, CreatedAt, LastChangedAt) " +
                "VALUES (@id, @symbol, 'XTEST', 'USD', 'Monitored', 900, @now, @now); " +
                "INSERT INTO ListingConfigurations (Id, Exchange, Symbol, Provider, ProviderInstrumentId, " +
                "ProviderSymbol, ProviderExchangeName, NativeQuoteCurrency, UnitSource, ConfirmedBy, ConfirmedAt, LastChangedAt) " +
                "VALUES (NEWID(), 'XTEST', @symbol, 'eToro', @providerId, @symbol, 'Test Exchange', 'USD', " +
                "'Administrator', 'synthetic-test', @now, @now);";
            command.Parameters.AddWithValue("@id", Guid.Parse($"00000000-0000-0000-0000-{index:D12}"));
            command.Parameters.AddWithValue("@symbol", $"DEMO{index:D5}");
            command.Parameters.AddWithValue("@providerId", index);
            command.Parameters.AddWithValue("@now", _clock.Now.ToDateTimeUtc());
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string table, string predicate = "1 = 1")
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {predicate}";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private TradingEngineDbContext Context()
    {
        return new TradingEngineDbContext(new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(_connectionString).Options);
    }
}
