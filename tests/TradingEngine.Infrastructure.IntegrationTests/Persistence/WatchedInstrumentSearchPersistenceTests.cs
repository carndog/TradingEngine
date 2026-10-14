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
public sealed class WatchedInstrumentSearchPersistenceTests
{
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

        await using TradingEngineDbContext context = CreateContext("Search");
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using TradingEngineDbContext context = CreateContext("Search");
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM MonitoringRuleRevisions; DELETE FROM MonitoringRules; " +
            "DELETE FROM ChartAnalysisDefinitions; DELETE FROM WatchedInstruments;");
    }

    [Test]
    public async Task SearchAsync_MatchesSymbolAndExchangeCaseInsensitively()
    {
        await SeedAsync(
            ("ALPHA-1", "XTEST", "USD"),
            ("BETA-1", "ALPHAX", "USD"),
            ("GAMMA-1", "XTEST", "USD"));

        Result<WatchedInstrumentPage> page = await SearchAsync("alpha", 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsSuccess, Is.True);
            Assert.That(page.Value.TotalCount, Is.EqualTo(2));
            Assert.That(
                page.Value.Items.Select(item => item.Symbol),
                Is.EquivalentTo(new[] { "ALPHA-1", "BETA-1" }));
        });
    }

    [Test]
    public async Task SearchAsync_ReturnsMatchesBeyondFirstPage()
    {
        List<(string Symbol, string Exchange, string Currency)> seeds = [];
        for (int index = 1; index <= 30; index++)
        {
            seeds.Add(($"SEED-{index:D3}", "XTEST", "USD"));
        }

        seeds.Add(("NEEDLE-1", "XTEST", "USD"));
        seeds.Add(("NEEDLE-2", "XTEST", "USD"));
        await SeedAsync(seeds.ToArray());

        Result<WatchedInstrumentPage> page = await SearchAsync("SEED", 2, 25);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsSuccess, Is.True);
            Assert.That(page.Value.TotalCount, Is.EqualTo(30));
            Assert.That(page.Value.Items, Has.Count.EqualTo(5));
            Assert.That(page.Value.Items.All(item => item.Symbol.StartsWith("SEED-")), Is.True);
        });
    }

    [Test]
    public async Task SearchAsync_OrdersDeterministicallyBySymbolExchangeCurrency()
    {
        await SeedAsync(
            ("BETA-1", "XTEST", "USD"),
            ("ALPHA-1", "ZTEST", "USD"),
            ("ALPHA-1", "XTEST", "USD"),
            ("ALPHA-1", "XTEST", "GBP"));

        Result<WatchedInstrumentPage> page = await SearchAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsSuccess, Is.True);
            Assert.That(page.Value.TotalCount, Is.EqualTo(4));
            Assert.That(
                page.Value.Items.Select(item => (item.Symbol, item.Exchange, item.QuoteCurrency)),
                Is.EqualTo(new[]
                {
                    ("ALPHA-1", "XTEST", "GBP"),
                    ("ALPHA-1", "XTEST", "USD"),
                    ("ALPHA-1", "ZTEST", "USD"),
                    ("BETA-1", "XTEST", "USD")
                }));
        });
    }

    [Test]
    public async Task SearchAsync_PageBeyondResults_ReturnsEmptyItemsWithTotal()
    {
        await SeedAsync(("ALPHA-1", "XTEST", "USD"));

        Result<WatchedInstrumentPage> page = await SearchAsync(null, 5, 25);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsSuccess, Is.True);
            Assert.That(page.Value.TotalCount, Is.EqualTo(1));
            Assert.That(page.Value.Items, Is.Empty);
            Assert.That(page.Value.Page, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task SearchAsync_WhenNothingMatches_ReturnsEmptyPage()
    {
        await SeedAsync(("ALPHA-1", "XTEST", "USD"));

        Result<WatchedInstrumentPage> page = await SearchAsync("missing", 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsSuccess, Is.True);
            Assert.That(page.Value.TotalCount, Is.EqualTo(0));
            Assert.That(page.Value.Items, Is.Empty);
        });
    }

    [Test]
    public async Task SearchAsync_TrimsQueryBeforeFiltering()
    {
        await SeedAsync(
            ("ALPHA-1", "XTEST", "USD"),
            ("GAMMA-1", "XTEST", "USD"));

        Result<WatchedInstrumentPage> page = await SearchAsync("  alpha  ", 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(page.IsSuccess, Is.True);
            Assert.That(page.Value.TotalCount, Is.EqualTo(1));
        });
    }

    private async Task<Result<WatchedInstrumentPage>> SearchAsync(
        string? query,
        int page,
        int pageSize)
    {
        await using TradingEngineDbContext context = CreateContext("Search");
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        return await store.SearchAsync(query, page, pageSize, CancellationToken.None);
    }

    private async Task SeedAsync(
        params (string Symbol, string Exchange, string Currency)[] instruments)
    {
        await using TradingEngineDbContext context = CreateContext("Search");
        SqlServerWatchedInstrumentStore store = new(context, _serializer);

        foreach ((string symbol, string exchange, string currency) in instruments)
        {
            WatchedInstrumentFields fields = WatchedInstrument
                .Validate(symbol, exchange, currency, 60)
                .Value;
            Result<Guid> added = await store.AddAsync(
                new WatchedInstrumentRegistration(
                    fields,
                    MonitoringState.Configured,
                    Instant.FromUtc(2026, 1, 2, 9, 30),
                    CreateDefinition()),
                CancellationToken.None);
            Assert.That(added.IsSuccess, Is.True);
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
