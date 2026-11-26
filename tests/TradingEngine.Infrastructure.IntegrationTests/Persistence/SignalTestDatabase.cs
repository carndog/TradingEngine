using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

internal sealed class SignalTestDatabase : IAsyncDisposable
{
    private MsSqlContainer? _container;
    private string? _connectionString;

    public static Instant TriggeredAt => Instant.FromUtc(2026, 1, 5, 12, 0);

    public async Task StartAsync(string? targetMigration = null)
    {
        string? server = Environment.GetEnvironmentVariable("TRADINGENGINE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(server))
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            await _container.StartAsync();
            server = _container.GetConnectionString();
        }

        _connectionString = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = $"SignalTest_{Guid.NewGuid():N}"
        }.ConnectionString;
        await using TradingEngineDbContext context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    public TradingEngineDbContext CreateContext()
    {
        return new TradingEngineDbContext(new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer(_connectionString ?? throw new InvalidOperationException("Start the test database first.")).Options);
    }

    public async Task<NotificationIntentMatchRow> SeedAsync()
    {
        NotificationIntentRow intent = await SeedIntentAsync();
        NotificationIntentMatchRow match = Match(intent);
        await using TradingEngineDbContext context = CreateContext();
        context.Attach(intent);
        context.Add(match);
        await context.SaveChangesAsync();
        return match;
    }

    public async Task<NotificationIntentRow> SeedIntentAsync()
    {
        await using TradingEngineDbContext context = CreateContext();
        WatchedInstrumentRow instrument = new()
        {
            Id = Guid.NewGuid(), Symbol = $"S{Guid.NewGuid():N}"[..12].ToUpperInvariant(), Exchange = "XTEST",
            QuoteCurrency = "USD", MonitoringState = "Monitored", SamplingIntervalSeconds = 900,
            CreatedAt = TriggeredAt, LastChangedAt = TriggeredAt
        };
        MonitoringRuleRow rule = new() { Id = Guid.NewGuid(), Instrument = instrument, CreatedAt = TriggeredAt };
        MonitoringRuleRevisionRow revision = new()
        {
            Id = Guid.NewGuid(), Rule = rule, CreatedAt = TriggeredAt, CreatedBy = "synthetic",
            DefinitionXml = "<ChartAnalysisDefinition priceScale=\"4\" />", EffectiveFrom = TriggeredAt,
            RevisionNumber = 1
        };
        NotificationIntentRow intent = new()
        {
            Id = Guid.NewGuid(), Instrument = instrument, ScheduledFor = TriggeredAt,
            MonitoringRuleRevisionId = revision.Id, Reason = "ConditionsTriggered", PriceBasis = "Bid",
            QuoteCurrency = "USD", CurrentBid = 100m, CurrentAsk = 101m, Status = "Pending",
            CreatedAt = TriggeredAt, UpdatedAt = TriggeredAt
        };
        context.Add(revision);
        context.Add(intent);
        await context.SaveChangesAsync();
        return intent;
    }

    public static NotificationIntentMatchRow Match(NotificationIntentRow intent)
    {
        return new NotificationIntentMatchRow
        {
            Id = Guid.NewGuid(), Intent = intent, ZoneId = "synthetic-zone", Condition = "BuyZone",
            ActionId = "publish-signal", Lower = 95m, Level = 100m, Upper = 105m, PreviousPrice = 94m, CurrentPrice = 100m
        };
    }

    public async Task<PriceObservationRow> QuoteAsync(Guid instrumentId, Instant at, decimal? bid = 110m,
        Instant? retrievedAt = null, Instant? providerAt = null)
    {
        await using TradingEngineDbContext context = CreateContext();
        PriceObservationRow quote = new()
        {
            Id = Guid.NewGuid(), WatchedInstrumentId = instrumentId, ScheduledFor = at, Provider = "synthetic",
            ProviderInstrumentId = 1, Bid = bid, Ask = 111m, QuoteCurrency = "USD", RetrievedAt = retrievedAt ?? at,
            ProviderObservedAt = providerAt, Timeliness = "Realtime", InvocationId = Guid.NewGuid()
        };
        context.Add(quote);
        await context.SaveChangesAsync();
        return quote;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else if (_connectionString is not null)
        {
            await using TradingEngineDbContext context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }
}
