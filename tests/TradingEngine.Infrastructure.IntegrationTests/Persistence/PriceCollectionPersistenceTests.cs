using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Domain.Instruments;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class PriceCollectionPersistenceTests
{
    private const string Database = "PriceCollection";
    private static readonly Instant CreatedAt = Instant.FromUtc(2026, 1, 2, 9, 30);
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
    public async Task ListMonitoredAsync_WhenStatesAndDeletionVary_ReturnsOnlyEnabledUndeleted()
    {
        Guid monitored = await InsertInstrumentAsync("MON", "Monitored", null);
        Guid configured = await InsertInstrumentAsync("CFG", "Configured", null);
        Guid deleted = await InsertInstrumentAsync(
            "DEL",
            "Monitored",
            CreatedAt.Plus(Duration.FromDays(1)));

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoredInstrumentSource source = new(context);

        IReadOnlyList<MonitoredInstrument> instruments =
            await source.ListMonitoredAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                instruments.Select(instrument => instrument.Id),
                Does.Contain(monitored));
            Assert.That(
                instruments.Select(instrument => instrument.Id),
                Does.Not.Contain(configured));
            Assert.That(
                instruments.Select(instrument => instrument.Id),
                Does.Not.Contain(deleted));

            MonitoredInstrument selected =
                instruments.Single(instrument => instrument.Id == monitored);
            Assert.That(selected.Symbol, Is.EqualTo("MON"));
            Assert.That(selected.Exchange, Is.EqualTo("XTEST"));
            Assert.That(selected.QuoteCurrency, Is.EqualTo("USD"));
            Assert.That(selected.SamplingIntervalSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenSlotFresh_PersistsNativePrecisionAndProvenance()
    {
        Guid instrumentId = await InsertInstrumentAsync("PREC", "Monitored", null);
        Guid invocationId = Guid.Parse("0a1b2c3d-4e5f-4a6b-9c8d-7e6f5a4b3c2d");
        Instant providerObservedAt = Instant.FromUtc(2026, 10, 30, 13, 45, 28);
        Instant retrievedAt = Instant.FromUtc(2026, 10, 30, 13, 45, 30);
        ScheduledPriceObservation observation = new(
            instrumentId,
            Slot,
            "eToro",
            4242,
            123.456789012345678m,
            124.000000000000001m,
            "GBX",
            providerObservedAt,
            retrievedAt,
            MarketQuoteTimeliness.Realtime,
            "synthetic-request-1",
            invocationId);

        bool recorded;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            recorded = await store.TryRecordObservationAndAttemptAsync(
                observation,
                CreateObservedAttempt(observation.InstrumentId, observation.ScheduledFor),
                CancellationToken.None);
        }

        Assert.That(recorded, Is.True);

        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Provider, ProviderInstrumentId, Bid, Ask, QuoteCurrency, " +
            "ProviderObservedAt, RetrievedAt, Timeliness, ProviderRequestId, InvocationId " +
            "FROM PriceObservations WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("eToro"));
            Assert.That(reader.GetInt64(1), Is.EqualTo(4242));
            Assert.That(reader.GetDecimal(2), Is.EqualTo(123.456789012345678m));
            Assert.That(reader.GetDecimal(3), Is.EqualTo(124.000000000000001m));
            Assert.That(reader.GetString(4), Is.EqualTo("GBX"));
            Assert.That(reader.GetDateTime(5), Is.EqualTo(providerObservedAt.ToDateTimeUtc()));
            Assert.That(reader.GetDateTime(6), Is.EqualTo(retrievedAt.ToDateTimeUtc()));
            Assert.That(reader.GetString(7), Is.EqualTo("Realtime"));
            Assert.That(reader.GetString(8), Is.EqualTo("synthetic-request-1"));
            Assert.That(reader.GetGuid(9), Is.EqualTo(invocationId));
        });
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenOneSidedAndProviderObservedAtNull_PersistsNulls()
    {
        Guid instrumentId = await InsertInstrumentAsync("ONE", "Monitored", null);
        ScheduledPriceObservation observation = new(
            instrumentId,
            Slot,
            "eToro",
            4242,
            987.123456789012345678m,
            null,
            "USD",
            null,
            Instant.FromUtc(2026, 10, 30, 13, 45, 30),
            MarketQuoteTimeliness.Delayed,
            null,
            Guid.NewGuid());

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            bool recorded = await store.TryRecordObservationAndAttemptAsync(
                observation,
                CreateObservedAttempt(observation.InstrumentId, observation.ScheduledFor),
                CancellationToken.None);
            Assert.That(recorded, Is.True);
        }

        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Bid, Ask, ProviderObservedAt, RetrievedAt, Timeliness, ProviderRequestId " +
            "FROM PriceObservations WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(reader.GetDecimal(0), Is.EqualTo(987.123456789012345678m));
            Assert.That(reader.IsDBNull(1), Is.True);
            Assert.That(reader.IsDBNull(2), Is.True);
            Assert.That(
                reader.GetDateTime(3),
                Is.EqualTo(Instant.FromUtc(2026, 10, 30, 13, 45, 30).ToDateTimeUtc()));
            Assert.That(reader.GetString(4), Is.EqualTo("Delayed"));
            Assert.That(reader.IsDBNull(5), Is.True);
        });
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenSlotAlreadyRecorded_ReturnsFalse()
    {
        Guid instrumentId = await InsertInstrumentAsync("DUP", "Monitored", null);

        bool first;
        bool second;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            first = await store.TryRecordObservationAndAttemptAsync(
                CreateObservation(instrumentId, Slot),
                CreateObservedAttempt(instrumentId, Slot),
                CancellationToken.None);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            second = await store.TryRecordObservationAndAttemptAsync(
                CreateObservation(instrumentId, Slot),
                CreateObservedAttempt(instrumentId, Slot),
                CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(
                ObservationCountFor(instrumentId),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenSecondSlotArrives_PersistsBoth()
    {
        Guid instrumentId = await InsertInstrumentAsync("TWO", "Monitored", null);
        Instant secondSlot = Slot.Plus(Duration.FromSeconds(60));

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            Assert.That(
                await store.TryRecordObservationAndAttemptAsync(
                    CreateObservation(instrumentId, Slot),
                    CreateObservedAttempt(instrumentId, Slot),
                    CancellationToken.None),
                Is.True);
            Assert.That(
                await store.TryRecordObservationAndAttemptAsync(
                    CreateObservation(instrumentId, secondSlot),
                    CreateObservedAttempt(instrumentId, secondSlot),
                    CancellationToken.None),
                Is.True);
        }

        Assert.That(ObservationCountFor(instrumentId), Is.EqualTo(2));
    }

    [Test]
    public async Task GetLatestScheduledForAsync_ReturnsLatestSlotPerInstrument()
    {
        Guid first = await InsertInstrumentAsync("LAT-A", "Monitored", null);
        Guid second = await InsertInstrumentAsync("LAT-B", "Monitored", null);
        Guid unseen = await InsertInstrumentAsync("LAT-C", "Monitored", null);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            await store.TryRecordObservationAndAttemptAsync(
                CreateObservation(first, Slot),
                CreateObservedAttempt(first, Slot),
                CancellationToken.None);
            await store.TryRecordObservationAndAttemptAsync(
                CreateObservation(first, Slot.Plus(Duration.FromSeconds(60))),
                CreateObservedAttempt(first, Slot.Plus(Duration.FromSeconds(60))),
                CancellationToken.None);
            await store.TryRecordObservationAndAttemptAsync(
                CreateObservation(second, Slot),
                CreateObservedAttempt(second, Slot),
                CancellationToken.None);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            IReadOnlyDictionary<Guid, Instant> latest =
                await store.GetLatestScheduledForAsync(
                    [first, second, unseen],
                    CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(latest, Has.Count.EqualTo(2));
                Assert.That(
                    latest[first],
                    Is.EqualTo(Slot.Plus(Duration.FromSeconds(60))));
                Assert.That(latest[second], Is.EqualTo(Slot));
                Assert.That(latest.ContainsKey(unseen), Is.False);
            });
        }
    }

    [Test]
    public async Task RecordAttemptAsync_PersistsOutcomeProvenanceAndError()
    {
        Guid instrumentId = await InsertInstrumentAsync("ATT", "Monitored", null);
        Guid invocationId = Guid.Parse("5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d");
        Instant attemptedAt = Instant.FromUtc(2026, 10, 30, 13, 45, 30);
        Instant recordedAt = Instant.FromUtc(2026, 10, 30, 13, 45, 31);
        PriceCollectionAttempt attempt = new(
            instrumentId,
            Slot,
            PriceCollectionOutcome.Throttled,
            "market_data.provider_throttled",
            "eToro",
            "synthetic-request-9",
            invocationId,
            attemptedAt,
            recordedAt);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            await store.RecordAttemptAsync(attempt, CancellationToken.None);
        }

        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Outcome, ErrorCode, Provider, ProviderRequestId, InvocationId, " +
            "AttemptedAt, RecordedAt, ScheduledFor " +
            "FROM PriceCollectionAttempts WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("Throttled"));
            Assert.That(reader.GetString(1), Is.EqualTo("market_data.provider_throttled"));
            Assert.That(reader.GetString(2), Is.EqualTo("eToro"));
            Assert.That(reader.GetString(3), Is.EqualTo("synthetic-request-9"));
            Assert.That(reader.GetGuid(4), Is.EqualTo(invocationId));
            Assert.That(reader.GetDateTime(5), Is.EqualTo(attemptedAt.ToDateTimeUtc()));
            Assert.That(reader.GetDateTime(6), Is.EqualTo(recordedAt.ToDateTimeUtc()));
            Assert.That(reader.GetDateTime(7), Is.EqualTo(Slot.ToDateTimeUtc()));
        });
    }

    [Test]
    public async Task RecordAttemptAsync_WhenOutcomeViolatesCheckConstraint_Throws()
    {
        Guid instrumentId = await InsertInstrumentAsync("CHK", "Monitored", null);

        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO PriceCollectionAttempts " +
            "(Id, WatchedInstrumentId, ScheduledFor, Outcome, InvocationId, " +
            "AttemptedAt, RecordedAt) " +
            "VALUES (@row, @id, '2026-10-30T13:45:00.0000000', 'Bogus', @invocation, " +
            "'2026-10-30T13:45:30.0000000', '2026-10-30T13:45:31.0000000')";
        command.Parameters.AddWithValue("@row", Guid.NewGuid());
        command.Parameters.AddWithValue("@id", instrumentId);
        command.Parameters.AddWithValue("@invocation", Guid.NewGuid());

        SqlException? violation = Assert.Throws<SqlException>(
            () => command.ExecuteNonQuery());

        Assert.That(violation!.Number, Is.EqualTo(547));
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenInstrumentMissing_ThrowsStorageException()
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerPriceObservationStore store = new(context);

        PriceObservationStorageException? failure =
            await Assert.ThrowsAsync<PriceObservationStorageException>(async () =>
                await store.TryRecordObservationAndAttemptAsync(
                    CreateObservation(
                        Guid.Parse("8f7e6d5c-4b3a-4a2b-9c8d-7e6f5a4b3c2d"),
                        Slot),
                    CreateObservedAttempt(
                        Guid.Parse("8f7e6d5c-4b3a-4a2b-9c8d-7e6f5a4b3c2d"),
                        Slot),
                    CancellationToken.None));

        Assert.That(failure!.InnerException, Is.InstanceOf<DbUpdateException>());
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenInstantIsSubTick_Throws()
    {
        Guid instrumentId = await InsertInstrumentAsync("TICK", "Monitored", null);
        ScheduledPriceObservation observation = CreateObservation(instrumentId, Slot)
            with
            {
                RetrievedAt = Slot.PlusNanoseconds(50)
            };

        await using TradingEngineDbContext context = CreateContext();
        SqlServerPriceObservationStore store = new(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await store.TryRecordObservationAndAttemptAsync(
                observation,
                CreateObservedAttempt(observation.InstrumentId, observation.ScheduledFor),
                CancellationToken.None));
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenAttemptInsertFails_RollsBackObservation()
    {
        Guid instrumentId = await InsertInstrumentAsync("ATO", "Monitored", null);
        await ExecuteNonQueryAsync(
            "CREATE TRIGGER TR_PCA_RejectMarker ON PriceCollectionAttempts " +
            "AFTER INSERT AS BEGIN " +
            "IF EXISTS (SELECT 1 FROM inserted WHERE ProviderRequestId = 'reject-marker') " +
            "THROW 51001, 'marker rejected', 1; END");

        try
        {
            PriceCollectionAttempt failingAttempt = CreateObservedAttempt(
                instrumentId,
                Slot) with
            {
                ProviderRequestId = "reject-marker"
            };

            await using (TradingEngineDbContext context = CreateContext())
            {
                SqlServerPriceObservationStore store = new(context);
                await Assert.ThrowsAsync<PriceObservationStorageException>(async () =>
                    await store.TryRecordObservationAndAttemptAsync(
                        CreateObservation(instrumentId, Slot),
                        failingAttempt,
                        CancellationToken.None));
            }

            Assert.Multiple(() =>
            {
                Assert.That(ObservationCountFor(instrumentId), Is.EqualTo(0));
                Assert.That(AttemptCountFor(instrumentId), Is.EqualTo(0));
            });

            await using (TradingEngineDbContext context = CreateContext())
            {
                SqlServerPriceObservationStore store = new(context);
                Assert.That(
                    await store.TryRecordObservationAndAttemptAsync(
                        CreateObservation(instrumentId, Slot),
                        CreateObservedAttempt(instrumentId, Slot),
                        CancellationToken.None),
                    Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(ObservationCountFor(instrumentId), Is.EqualTo(1));
                Assert.That(AttemptCountFor(instrumentId), Is.EqualTo(1));
            });
        }
        finally
        {
            await ExecuteNonQueryAsync(
                "DROP TRIGGER TR_PCA_RejectMarker");
        }
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenWritersRaceSameSlot_PersistsSinglePair()
    {
        Guid instrumentA = await InsertInstrumentAsync("RACE-A", "Monitored", null);
        Guid instrumentB = await InsertInstrumentAsync("RACE-B", "Monitored", null);

        bool[] outcomes;
        await using (TradingEngineDbContext contextA = CreateContext())
        await using (TradingEngineDbContext contextB = CreateContext())
        {
            SqlServerPriceObservationStore storeA = new(contextA);
            SqlServerPriceObservationStore storeB = new(contextB);

            Task<bool> first = storeA.TryRecordObservationAndAttemptAsync(
                CreateObservation(instrumentA, Slot),
                CreateObservedAttempt(instrumentA, Slot),
                CancellationToken.None);
            Task<bool> second = storeB.TryRecordObservationAndAttemptAsync(
                CreateObservation(instrumentA, Slot),
                CreateObservedAttempt(instrumentA, Slot),
                CancellationToken.None);

            outcomes = await Task.WhenAll(first, second);

            Assert.That(outcomes.Count(outcome => outcome), Is.EqualTo(1));

            SqlServerPriceObservationStore loser =
                outcomes[0] ? storeB : storeA;
            await loser.RecordAttemptAsync(
                CreateAttempt(
                    instrumentA,
                    Slot,
                    PriceCollectionOutcome.SkippedDuplicate,
                    null,
                    Instant.FromUtc(2026, 10, 30, 13, 45, 31)),
                CancellationToken.None);

            bool recovered = await loser.TryRecordObservationAndAttemptAsync(
                CreateObservation(instrumentB, Slot),
                CreateObservedAttempt(instrumentB, Slot),
                CancellationToken.None);
            Assert.That(recovered, Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(ObservationCountFor(instrumentA), Is.EqualTo(1));
            Assert.That(ObservationCountFor(instrumentB), Is.EqualTo(1));
            Assert.That(AttemptCountFor(instrumentA), Is.EqualTo(2));
            Assert.That(
                AttemptOutcomeCounts(instrumentA, "Observed"),
                Is.EqualTo(1));
            Assert.That(
                AttemptOutcomeCounts(instrumentA, "SkippedDuplicate"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TryRecordObservationAndAttemptAsync_WhenQuoteScaleExceedsStorage_ThrowsWithoutPersisting()
    {
        Guid instrumentId = await InsertInstrumentAsync("SCALE", "Monitored", null);
        ScheduledPriceObservation observation = CreateObservation(instrumentId, Slot)
            with
            {
                Bid = 0.0000000000000000001m
            };

        await using TradingEngineDbContext context = CreateContext();
        SqlServerPriceObservationStore store = new(context);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.TryRecordObservationAndAttemptAsync(
                observation,
                CreateObservedAttempt(instrumentId, Slot),
                CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ObservationCountFor(instrumentId), Is.EqualTo(0));
            Assert.That(AttemptCountFor(instrumentId), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GetLatestAttemptsAsync_ReturnsMostRecentAttemptPerInstrument()
    {
        Guid first = await InsertInstrumentAsync("ATT-A", "Monitored", null);
        Guid second = await InsertInstrumentAsync("ATT-B", "Monitored", null);
        Guid unseen = await InsertInstrumentAsync("ATT-C", "Monitored", null);

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            await store.RecordAttemptAsync(
                CreateAttempt(
                    first,
                    Slot,
                    PriceCollectionOutcome.SkippedMarketClosed,
                    null,
                    Instant.FromUtc(2026, 10, 30, 13, 50)),
                CancellationToken.None);
            await store.RecordAttemptAsync(
                CreateAttempt(
                    first,
                    Slot.Plus(Duration.FromSeconds(60)),
                    PriceCollectionOutcome.Throttled,
                    "market_data.provider_throttled",
                    Instant.FromUtc(2026, 10, 30, 13, 46)),
                CancellationToken.None);
            await store.RecordAttemptAsync(
                CreateAttempt(
                    second,
                    Slot,
                    PriceCollectionOutcome.Observed,
                    null,
                    Instant.FromUtc(2026, 10, 30, 13, 45)),
                CancellationToken.None);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerPriceObservationStore store = new(context);
            IReadOnlyDictionary<Guid, PriceCollectionAttempt> latest =
                await store.GetLatestAttemptsAsync(
                    [first, second, unseen],
                    CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(latest, Has.Count.EqualTo(2));
                Assert.That(
                    latest[first].Outcome,
                    Is.EqualTo(PriceCollectionOutcome.SkippedMarketClosed));
                Assert.That(latest[first].ScheduledFor, Is.EqualTo(Slot));
                Assert.That(
                    latest[second].Outcome,
                    Is.EqualTo(PriceCollectionOutcome.Observed));
                Assert.That(latest.ContainsKey(unseen), Is.False);
            });
        }
    }

    private static ScheduledPriceObservation CreateObservation(Guid instrumentId, Instant slot)
    {
        return new ScheduledPriceObservation(
            instrumentId,
            slot,
            "eToro",
            4242,
            123.456789012345678m,
            124.000000000000001m,
            "USD",
            Instant.FromUtc(2026, 10, 30, 13, 45, 28),
            Instant.FromUtc(2026, 10, 30, 13, 45, 30),
            MarketQuoteTimeliness.Realtime,
            "synthetic-request",
            Guid.NewGuid());
    }

    private static PriceCollectionAttempt CreateObservedAttempt(
        Guid instrumentId,
        Instant slot)
    {
        return CreateAttempt(
            instrumentId,
            slot,
            PriceCollectionOutcome.Observed,
            null,
            Instant.FromUtc(2026, 10, 30, 13, 45, 30));
    }

    private static PriceCollectionAttempt CreateAttempt(
        Guid instrumentId,
        Instant slot,
        PriceCollectionOutcome outcome,
        string? errorCode,
        Instant recordedAt)
    {
        return new PriceCollectionAttempt(
            instrumentId,
            slot,
            outcome,
            errorCode,
            "eToro",
            "synthetic-request",
            Guid.NewGuid(),
            Instant.FromUtc(2026, 10, 30, 13, 45, 30),
            recordedAt);
    }

    private int ObservationCountFor(Guid instrumentId)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM PriceObservations WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);
        return (int)command.ExecuteScalar()!;
    }

    private int AttemptCountFor(Guid instrumentId)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM PriceCollectionAttempts WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);
        return (int)command.ExecuteScalar()!;
    }

    private int AttemptOutcomeCounts(Guid instrumentId, string outcome)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM PriceCollectionAttempts " +
            "WHERE WatchedInstrumentId = @id AND Outcome = @outcome";
        command.Parameters.AddWithValue("@id", instrumentId);
        command.Parameters.AddWithValue("@outcome", outcome);
        return (int)command.ExecuteScalar()!;
    }

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Guid> InsertInstrumentAsync(
        string symbol,
        string monitoringState,
        Instant? deletedAt)
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
        command.Parameters.AddWithValue("@symbol", symbol);
        command.Parameters.AddWithValue("@state", monitoringState);
        command.Parameters.AddWithValue(
            "@deletedAt",
            deletedAt is null
                ? DBNull.Value
                : deletedAt.Value.ToDateTimeUtc());
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
