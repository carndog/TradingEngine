using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class PriceObservationHistoryPersistenceTests
{
    private const string Database = "PriceObservationHistory";
    private static readonly Instant Slot = Instant.FromUtc(2026, 10, 30, 13, 45);

    private MsSqlContainer _container = null!;
    private string _connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        await using TradingEngineDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task GetHistoryAsync_TwoSlotsWithEqualPrices_ReturnsBothNewestFirst()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        Instant newer = Slot.Plus(Duration.FromMinutes(1));
        await InsertObservationAsync(instrumentId, Slot, 10.5m, 10.75m);
        await InsertObservationAsync(instrumentId, newer, 10.5m, 10.75m);

        PriceObservationHistory history = await ReadAsync(instrumentId, null, 25);

        Assert.Multiple(() =>
        {
            Assert.That(history.Items.Select(item => item.ScheduledFor), Is.EqualTo(new[] { newer, Slot }));
            Assert.That(history.Items.Select(item => item.Bid), Is.EqualTo(new decimal?[] { 10.5m, 10.5m }));
            Assert.That(history.Latest!.ScheduledFor, Is.EqualTo(newer));
            Assert.That(history.NextBefore, Is.Null);
        });
    }

    [Test]
    public async Task GetHistoryAsync_LatestHasOnlyBid_DoesNotBackfillAskFromOlderRow()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        Instant newer = Slot.Plus(Duration.FromMinutes(1));
        await InsertObservationAsync(instrumentId, Slot, 1.1m, 1.2m);
        await InsertObservationAsync(instrumentId, newer, 1.15m, null);

        PriceObservationHistory history = await ReadAsync(instrumentId, null, 25);

        Assert.Multiple(() =>
        {
            Assert.That(history.Latest!.Bid, Is.EqualTo(1.15m));
            Assert.That(history.Latest.Ask, Is.Null);
        });
    }

    [Test]
    public async Task GetHistoryAsync_LatestHasOnlyAsk_DoesNotBackfillBidFromOlderRow()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        Instant newer = Slot.Plus(Duration.FromMinutes(1));
        await InsertObservationAsync(instrumentId, Slot, 1.1m, 1.2m);
        await InsertObservationAsync(instrumentId, newer, null, 1.25m);

        PriceObservationHistory history = await ReadAsync(instrumentId, null, 25);

        Assert.Multiple(() =>
        {
            Assert.That(history.Latest!.Bid, Is.Null);
            Assert.That(history.Latest.Ask, Is.EqualTo(1.25m));
        });
    }

    [Test]
    public async Task GetHistoryAsync_StoredValues_PreservesPrecisionUnitsTimesAndProvenance()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        Instant scheduled = Slot.PlusTicks(1234567);
        Instant providerObservedAt = Slot.Plus(Duration.FromSeconds(28)).PlusTicks(7654321);
        Instant retrievedAt = Slot.Plus(Duration.FromSeconds(30)).PlusTicks(1);
        await InsertObservationAsync(
            instrumentId,
            scheduled,
            123.456789012345678901m,
            0.000000000000000001m,
            "GBX",
            providerObservedAt,
            retrievedAt,
            "Delayed");
        await InsertObservationAsync(instrumentId, Slot, 1m, 2m);

        PriceObservationHistory history = await ReadAsync(instrumentId, null, 25);
        PriceObservationRecord newest = history.Items[0];
        PriceObservationRecord older = history.Items[1];

        Assert.Multiple(() =>
        {
            Assert.That(newest.Bid, Is.EqualTo(123.456789012345678901m));
            Assert.That(newest.Ask, Is.EqualTo(0.000000000000000001m));
            Assert.That(newest.QuoteCurrency, Is.EqualTo("GBX"));
            Assert.That(newest.Provider, Is.EqualTo("synthetic"));
            Assert.That(newest.ScheduledFor, Is.EqualTo(scheduled));
            Assert.That(newest.ProviderObservedAt, Is.EqualTo(providerObservedAt));
            Assert.That(newest.RetrievedAt, Is.EqualTo(retrievedAt));
            Assert.That(newest.Timeliness, Is.EqualTo(MarketQuoteTimeliness.Delayed));
            Assert.That(older.ProviderObservedAt, Is.Null);
            Assert.That(older.Timeliness, Is.EqualTo(MarketQuoteTimeliness.Realtime));
        });
    }

    [Test]
    public async Task GetHistoryAsync_NoObservations_ReturnsEmptySuccess()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);

        PriceObservationHistory history = await ReadAsync(instrumentId, null, 25);

        Assert.Multiple(() =>
        {
            Assert.That(history.Latest, Is.Null);
            Assert.That(history.Items, Is.Empty);
            Assert.That(history.NextBefore, Is.Null);
        });
    }

    [Test]
    public async Task GetHistoryAsync_UnknownInstrument_ReturnsNotFound()
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerPriceObservationHistoryReader reader = new(context);

        Result<PriceObservationHistory> result = await reader.GetHistoryAsync(
            Guid.NewGuid(), null, 25, CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(PriceObservationErrors.InstrumentNotFound));
    }

    [Test]
    public async Task GetHistoryAsync_DeletedInstrument_ReturnsNotFound()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", Slot);
        await using TradingEngineDbContext context = CreateContext();
        SqlServerPriceObservationHistoryReader reader = new(context);

        Result<PriceObservationHistory> result = await reader.GetHistoryAsync(
            instrumentId, null, 25, CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(PriceObservationErrors.InstrumentNotFound));
    }

    [Test]
    public async Task GetHistoryAsync_ConfiguredInstrument_KeepsReadableHistory()
    {
        Guid instrumentId = await InsertInstrumentAsync("Configured", null);
        await InsertObservationAsync(instrumentId, Slot, 5m, 6m);

        PriceObservationHistory history = await ReadAsync(instrumentId, null, 25);

        Assert.That(history.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetHistoryAsync_IdenticalSlotsAcrossInstruments_ReturnsOnlyRequestedInstrument()
    {
        Guid requested = await InsertInstrumentAsync("Monitored", null);
        Guid other = await InsertInstrumentAsync("Monitored", null);
        Instant later = Slot.Plus(Duration.FromMinutes(5));
        await InsertObservationAsync(requested, Slot, 1m, 2m);
        await InsertObservationAsync(other, Slot, 91m, 92m);
        await InsertObservationAsync(other, later, 93m, 94m);

        PriceObservationHistory history = await ReadAsync(requested, null, 25);
        PriceObservationHistory olderPage = await ReadAsync(requested, later, 25);

        Assert.Multiple(() =>
        {
            Assert.That(history.Items.Select(item => item.Bid), Is.EqualTo(new decimal?[] { 1m }));
            Assert.That(history.Latest!.Bid, Is.EqualTo(1m));
            Assert.That(olderPage.Items.Select(item => item.Bid), Is.EqualTo(new decimal?[] { 1m }));
            Assert.That(olderPage.Latest!.Bid, Is.EqualTo(1m));
        });
    }

    [Test]
    public async Task GetHistoryAsync_PagedWithCursor_ReturnsBoundedPagesUntilFinal()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        Instant[] slots = Enumerable.Range(0, 5)
            .Select(index => Slot.Plus(Duration.FromMinutes(index)))
            .ToArray();
        foreach (Instant slot in slots)
        {
            await InsertObservationAsync(instrumentId, slot, 1m, 2m);
        }

        PriceObservationHistory first = await ReadAsync(instrumentId, null, 2);
        PriceObservationHistory second = await ReadAsync(instrumentId, first.NextBefore, 2);
        PriceObservationHistory final = await ReadAsync(instrumentId, second.NextBefore, 2);

        Assert.Multiple(() =>
        {
            Assert.That(first.Items.Select(item => item.ScheduledFor), Is.EqualTo(new[] { slots[4], slots[3] }));
            Assert.That(first.NextBefore, Is.EqualTo(slots[3]));
            Assert.That(second.Items.Select(item => item.ScheduledFor), Is.EqualTo(new[] { slots[2], slots[1] }));
            Assert.That(second.Latest!.ScheduledFor, Is.EqualTo(slots[4]));
            Assert.That(final.Items.Select(item => item.ScheduledFor), Is.EqualTo(new[] { slots[0] }));
            Assert.That(final.NextBefore, Is.Null);
        });
    }

    [Test]
    public async Task GetHistoryAsync_PageExactlyFull_ReportsNoOlderPage()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        await InsertObservationAsync(instrumentId, Slot, 1m, 2m);
        await InsertObservationAsync(instrumentId, Slot.Plus(Duration.FromMinutes(1)), 1m, 2m);

        PriceObservationHistory history = await ReadAsync(instrumentId, null, 2);

        Assert.Multiple(() =>
        {
            Assert.That(history.Items, Has.Count.EqualTo(2));
            Assert.That(history.NextBefore, Is.Null);
        });
    }

    [Test]
    public async Task GetHistoryAsync_NewerRowInsertedBetweenPages_DoesNotRepeatOrSkipRows()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        Instant[] slots = Enumerable.Range(0, 4)
            .Select(index => Slot.Plus(Duration.FromMinutes(index)))
            .ToArray();
        foreach (Instant slot in slots)
        {
            await InsertObservationAsync(instrumentId, slot, 1m, 2m);
        }

        PriceObservationHistory first = await ReadAsync(instrumentId, null, 2);
        Instant inserted = Slot.Plus(Duration.FromMinutes(10));
        await InsertObservationAsync(instrumentId, inserted, 3m, 4m);
        PriceObservationHistory second = await ReadAsync(instrumentId, first.NextBefore, 2);

        Instant[] seen = first.Items.Concat(second.Items).Select(item => item.ScheduledFor).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new[] { slots[3], slots[2], slots[1], slots[0] }));
            Assert.That(second.Latest!.ScheduledFor, Is.EqualTo(inserted));
        });
    }

    [Test]
    public async Task GetHistoryAsync_Read_DoesNotMutatePersistence()
    {
        Guid instrumentId = await InsertInstrumentAsync("Monitored", null);
        await InsertObservationAsync(instrumentId, Slot, 1m, 2m);
        int observationsBefore = await CountAsync("PriceObservations");
        int attemptsBefore = await CountAsync("PriceCollectionAttempts");

        await ReadAsync(instrumentId, null, 25);
        await ReadAsync(instrumentId, null, 25);

        Assert.Multiple(async () =>
        {
            Assert.That(await CountAsync("PriceObservations"), Is.EqualTo(observationsBefore));
            Assert.That(await CountAsync("PriceCollectionAttempts"), Is.EqualTo(attemptsBefore));
        });
    }

    [Test]
    public void GetHistoryAsync_CancelledToken_Throws()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.That(
            async () =>
            {
                await using TradingEngineDbContext context = CreateContext();
                SqlServerPriceObservationHistoryReader reader = new(context);
                await reader.GetHistoryAsync(Guid.NewGuid(), null, 25, source.Token);
            },
            Throws.InstanceOf<OperationCanceledException>());
    }

    private async Task<PriceObservationHistory> ReadAsync(Guid instrumentId, Instant? before, int pageSize)
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerPriceObservationHistoryReader reader = new(context);
        Result<PriceObservationHistory> result = await reader.GetHistoryAsync(
            instrumentId, before, pageSize, CancellationToken.None);
        return result.Value;
    }

    private async Task<int> CountAsync(string table)
    {
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        object? scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt32(scalar);
    }

    private async Task InsertObservationAsync(
        Guid instrumentId,
        Instant scheduledFor,
        decimal? bid,
        decimal? ask,
        string quoteCurrency = "USD",
        Instant? providerObservedAt = null,
        Instant? retrievedAt = null,
        string timeliness = "Realtime")
    {
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO PriceObservations " +
            "(Id, WatchedInstrumentId, ScheduledFor, Provider, ProviderInstrumentId, Bid, Ask, " +
            "QuoteCurrency, ProviderObservedAt, RetrievedAt, Timeliness, ProviderRequestId, InvocationId) " +
            "VALUES (@id, @instrumentId, @scheduledFor, 'synthetic', 4242, @bid, @ask, " +
            "@quoteCurrency, @providerObservedAt, @retrievedAt, @timeliness, NULL, @invocationId)";
        command.Parameters.AddWithValue("@id", Guid.NewGuid());
        command.Parameters.AddWithValue("@instrumentId", instrumentId);
        AddDateTime2(command, "@scheduledFor", scheduledFor);
        AddDecimal(command, "@bid", bid);
        AddDecimal(command, "@ask", ask);
        command.Parameters.AddWithValue("@quoteCurrency", quoteCurrency);
        AddDateTime2(command, "@providerObservedAt", providerObservedAt);
        AddDateTime2(command, "@retrievedAt", retrievedAt ?? scheduledFor.Plus(Duration.FromSeconds(2)));
        command.Parameters.AddWithValue("@timeliness", timeliness);
        command.Parameters.AddWithValue("@invocationId", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static void AddDateTime2(SqlCommand command, string name, Instant? value)
    {
        SqlParameter parameter = command.Parameters.Add(name, System.Data.SqlDbType.DateTime2);
        parameter.Scale = 7;
        parameter.Value = value is null ? DBNull.Value : value.Value.ToDateTimeUtc();
    }

    private static void AddDecimal(SqlCommand command, string name, decimal? value)
    {
        SqlParameter parameter = command.Parameters.Add(name, System.Data.SqlDbType.Decimal);
        parameter.Precision = 38;
        parameter.Scale = 18;
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }

    private async Task<Guid> InsertInstrumentAsync(string monitoringState, Instant? deletedAt)
    {
        Guid id = Guid.NewGuid();
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO WatchedInstruments " +
            "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, " +
            "SamplingIntervalSeconds, CreatedAt, LastChangedAt, DeletedAt) " +
            "VALUES (@id, @symbol, 'XTEST', 'USD', @state, 60, " +
            "'2026-01-02T09:30:00.0000000', '2026-01-02T09:30:00.0000000', @deletedAt)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@symbol", $"H{id.ToString("N")[..8]}");
        command.Parameters.AddWithValue("@state", monitoringState);
        AddDateTime2(command, "@deletedAt", deletedAt);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(ConnectionStringFor())
                .Options;

        return new TradingEngineDbContext(options);
    }

    private string ConnectionStringFor()
    {
        return $"{_connectionString};Database={Database}";
    }
}
