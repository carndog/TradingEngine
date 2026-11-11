using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Recommendations;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class RecommendationPipelinePersistenceTests
{
    private const string Database = "RecommendationPipeline";
    private static readonly Instant T0 = Instant.FromUtc(2026, 10, 30, 13, 0, 0);
    private static readonly Guid PhantomRevisionId =
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private const string ConnectionStringVariable =
        "TRADINGENGINE_TEST_CONNECTION_STRING";

    private MsSqlContainer? _container;
    private string _connectionString = null!;
    private string _suffix = null!;
    private ChartAnalysisDefinitionXmlSerializer _serializer = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        string? overrideConnection = Environment.GetEnvironmentVariable(
            ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(overrideConnection))
        {
            _container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }
        else
        {
            _connectionString = overrideConnection;
        }

        _suffix = Guid.NewGuid().ToString("N")[..8];
        _serializer = new ChartAnalysisDefinitionXmlSerializer();

        await using TradingEngineDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await ExecuteNonQueryAsync(
            "DELETE FROM NotificationAttempts; " +
            "DELETE FROM NotificationIntentMatches; " +
            "DELETE FROM NotificationIntents; " +
            "DELETE FROM RecommendationCheckpoints; " +
            "DELETE FROM PriceCollectionAttempts; " +
            "DELETE FROM PriceObservations; " +
            "DELETE FROM MonitoringRuleRevisions; " +
            "DELETE FROM ChartAnalysisDefinitions; " +
            "DELETE FROM MonitoringRules; " +
            "DELETE FROM WatchedInstruments;");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task HandleAsync_WhenSequenceReEnters_CommitsTwoIntentsAndTwoAcceptedSends()
    {
        Guid instrumentId = await InsertInstrumentAsync($"SEQ-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();

        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), 97m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(3)), 110m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(4)), 102m);
        EvaluationRunResult evaluation = await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        DispatchRunResult dispatch = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.Items[0].IntentsCreated, Is.EqualTo(2));
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(2));
            Assert.That(IntentCountAt(instrumentId, T0.Plus(Duration.FromMinutes(1))), Is.EqualTo(1));
            Assert.That(IntentCountAt(instrumentId, T0.Plus(Duration.FromMinutes(4))), Is.EqualTo(1));
            Assert.That(
                CheckpointFor(instrumentId),
                Is.EqualTo(T0.Plus(Duration.FromMinutes(4)).ToDateTimeUtc()));

            Assert.That(dispatch.Items, Has.Count.EqualTo(2));
            Assert.That(
                dispatch.Items.Count(item => item.Outcome == DispatchItemOutcome.Sent),
                Is.EqualTo(2));
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(AttemptCountForStatus(instrumentId, "Submitted"), Is.EqualTo(2));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_WhenReplayedAndWorkerRestarted_CountsStayAtTwo()
    {
        Guid instrumentId = await InsertInstrumentAsync($"RPL-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), 97m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(3)), 110m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(4)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender);
        EvaluationRunResult replayedEvaluation = await RunEvaluationAsync();
        DispatchRunResult replayedDispatch = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(replayedEvaluation.Items[0].IntentsCreated, Is.EqualTo(0));
            Assert.That(replayedDispatch.Items, Is.Empty);
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(2));
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_WhenWorkersRace_SingleIntentPerTrigger()
    {
        Guid instrumentId = await InsertInstrumentAsync($"RCW-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), 97m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(3)), 110m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(4)), 102m);

        await using TradingEngineDbContext contextA = CreateContext();
        await using TradingEngineDbContext contextB = CreateContext();

        Task<EvaluationRunResult> first = CreateHandler(contextA).HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);
        Task<EvaluationRunResult> second = CreateHandler(contextB).HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        await Task.WhenAll(first, second);

        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender);
        await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(2));
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(
                CheckpointFor(instrumentId),
                Is.EqualTo(T0.Plus(Duration.FromMinutes(4)).ToDateTimeUtc()));
        });
    }

    [Test]
    public async Task HandleAsync_WhenRevisionBoundaryCrossed_PersistsExactRevisionIds()
    {
        Guid instrumentId = await InsertInstrumentAsync($"REV-{_suffix}", "Configured");
        Instant boundary = T0.Plus(Duration.FromMinutes(3));
        (Guid revisionOneId, Guid revisionTwoId) =
            await AddTwoRevisionRuleAsync(instrumentId, boundary);
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), 110m);
        await AddObservationAsync(instrumentId, boundary, 120m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(4)), 102m);
        await RunEvaluationAsync();

        Guid first = RevisionIdFor(instrumentId, T0.Plus(Duration.FromMinutes(1)));
        Guid second = RevisionIdFor(instrumentId, T0.Plus(Duration.FromMinutes(4)));

        Assert.Multiple(() =>
        {
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(2));
            Assert.That(first, Is.EqualTo(revisionOneId));
            Assert.That(second, Is.EqualTo(revisionTwoId));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSendAmbiguous_ResolvesViaProbeWithoutSecondAcceptance()
    {
        Guid instrumentId = await InsertInstrumentAsync($"AMB-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.Ambiguous, null, "timeout"));
        DispatchRunResult first = await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        DispatchRunResult second = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(
                first.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(
                second.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(sender.Sends, Has.Count.EqualTo(1));
            Assert.That(sender.Probes, Has.Count.EqualTo(1));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(1));
            Assert.That(
                AttemptCountForStatus(instrumentId, "ResolvedSucceeded"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenEvaluationInputInvalid_RetriesSlotAfterRepair()
    {
        Guid instrumentId = await InsertInstrumentAsync($"BAD-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), null, null);

        EvaluationRunResult failed = await RunEvaluationAsync();

        Assert.Multiple(() =>
        {
            Assert.That(failed.Items[0].FailureCode, Is.Not.Null);
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(0));
            Assert.That(CheckpointFor(instrumentId), Is.EqualTo(T0.ToDateTimeUtc()));
        });

        await RepairObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        EvaluationRunResult recovered = await RunEvaluationAsync();

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Items[0].FailureCode, Is.Null);
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(1));
            Assert.That(
                CheckpointFor(instrumentId),
                Is.EqualTo(T0.Plus(Duration.FromMinutes(1)).ToDateTimeUtc()));
        });
    }

    [Test]
    public async Task HandleAsync_WhenPriceOnlyInstrument_CollectsWithoutNotifications()
    {
        Guid instrumentId = await InsertInstrumentAsync($"QUIET-{_suffix}", "Monitored");
        await AddObservationAsync(instrumentId, T0, 120m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);

        EvaluationRunResult result = await RunEvaluationAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items.Any(item => item.InstrumentId == instrumentId),
                Is.False);
            Assert.That(
                ScalarInt(
                    "SELECT COUNT(*) FROM RecommendationCheckpoints " +
                    "WHERE WatchedInstrumentId = @id",
                    instrumentId),
                Is.EqualTo(0));
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleAsync_WhenFirstEnabled_SeedsBaselineWithoutFlood()
    {
        Guid instrumentId = await InsertInstrumentAsync($"BASE-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), 97m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(3)), 110m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(4)), 102m);

        EvaluationRunResult result = await RunEvaluationAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].IntentsCreated, Is.EqualTo(0));
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(0));
            Assert.That(
                CheckpointFor(instrumentId),
                Is.EqualTo(T0.Plus(Duration.FromMinutes(4)).ToDateTimeUtc()));
        });
    }

    [Test]
    public async Task CommitEvaluationAsync_WhenPhantomIntentExists_HealsCheckpoint()
    {
        Guid instrumentId = await InsertInstrumentAsync($"HL-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);

        Instant slot = T0.Plus(Duration.FromMinutes(1));
        await ExecuteNonQueryAsync(
            "INSERT INTO NotificationIntents " +
            "(Id, WatchedInstrumentId, ScheduledFor, PreviousScheduledFor, " +
            "MonitoringRuleRevisionId, Reason, PriceBasis, QuoteCurrency, " +
            "Status, CreatedAt, UpdatedAt) VALUES (" +
            $"NEWID(), '{instrumentId}', " +
            $"'{slot.ToDateTimeUtc():yyyy-MM-ddTHH:mm:ss.fffffff}', " +
            $"'{T0.ToDateTimeUtc():yyyy-MM-ddTHH:mm:ss.fffffff}', " +
            $"'{PhantomRevisionId}', 'ConditionsTriggered', 'Bid', 'USD', 'Pending', " +
            "'2026-10-30T13:45:00.0000000', '2026-10-30T13:45:00.0000000')");

        await using TradingEngineDbContext context = CreateContext();
        SqlServerRecommendationStore store = new(context);
        EvaluationCommitOutcome outcome = await store.CommitEvaluationAsync(
            new RecommendationCommit(
                instrumentId,
                T0,
                slot,
                Instant.FromUtc(2026, 10, 30, 13, 46),
                new TriggeredRecommendation(
                    instrumentId,
                    slot,
                    T0,
                    PhantomRevisionId,
                    PriceBasis.Bid,
                    "USD",
                    100m,
                    101m,
                    [])),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(EvaluationCommitOutcome.AlreadyRecorded));
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(1));
            Assert.That(CheckpointFor(instrumentId), Is.EqualTo(slot.ToDateTimeUtc()));
        });
    }

    [Test]
    public async Task TryClaimAsync_WhenLeaseHeld_SecondClaimFails()
    {
        Guid instrumentId = await InsertInstrumentAsync($"CLM-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        Guid intentId = IntentIdFor(instrumentId, T0.Plus(Duration.FromMinutes(1)));
        Instant now = Instant.FromUtc(2026, 10, 30, 13, 50);
        Instant until = now.Plus(Duration.FromSeconds(120));

        bool claimedFirst;
        bool claimedSecond;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerNotificationIntentStore store = new(context);
            claimedFirst = await store.TryClaimAsync(
                intentId,
                Guid.NewGuid(),
                until,
                now,
                CancellationToken.None);
            claimedSecond = await store.TryClaimAsync(
                intentId,
                Guid.NewGuid(),
                until,
                now,
                CancellationToken.None);
        }

        bool claimedAfterExpiry;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerNotificationIntentStore store = new(context);
            claimedAfterExpiry = await store.TryClaimAsync(
                intentId,
                Guid.NewGuid(),
                until,
                until.Plus(Duration.FromSeconds(1)),
                CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(claimedFirst, Is.True);
            Assert.That(claimedSecond, Is.False);
            Assert.That(claimedAfterExpiry, Is.True);
        });
    }

    private async Task<EvaluationRunResult> RunEvaluationAsync()
    {
        await using TradingEngineDbContext context = CreateContext();
        return await CreateHandler(context).HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);
    }

    private EvaluateSavedPricesHandler CreateHandler(TradingEngineDbContext context)
    {
        return new EvaluateSavedPricesHandler(
            new SqlServerRecommendationStore(context),
            new SqlServerMonitoringRuleStore(context, _serializer),
            new ChartAnalysisEvaluator(),
            SystemClock.Instance,
            TimeProvider.System,
            new RecommendationOptions
            {
                PriceBasis = "Bid",
                MaxExecutionTimeSeconds = 60
            });
    }

    private async Task<DispatchRunResult> RunDispatchAsync(RecordingEmailSender sender)
    {
        await using TradingEngineDbContext context = CreateContext();
        DispatchPendingNotificationsHandler handler = new(
            new SqlServerNotificationIntentStore(context),
            sender,
            new RecommendationEmailRenderer(),
            SystemClock.Instance,
            new NotificationOptions { MaxDispatchAttempts = 3 });

        return await handler.HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);
    }

    private async Task<Guid> InsertInstrumentAsync(string symbol, string monitoringState)
    {
        Guid id = Guid.NewGuid();
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO WatchedInstruments " +
            "(Id, Symbol, Exchange, QuoteCurrency, MonitoringState, " +
            "SamplingIntervalSeconds, CreatedAt, LastChangedAt) " +
            "VALUES (@id, @symbol, 'XTEST', 'USD', @state, 60, " +
            "'2026-01-02T09:30:00.0000000', '2026-01-02T09:30:00.0000000')";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@symbol", symbol);
        command.Parameters.AddWithValue("@state", monitoringState);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task AddRuleAsync(
        Guid instrumentId,
        ChartAnalysisDefinition definition)
    {
        MonitoringRule rule = MonitoringRule.Create(
            Guid.CreateVersion7(),
            instrumentId,
            Guid.CreateVersion7(),
            definition,
            T0,
            "synthetic").Value;

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);
        Result added = await store.AddAsync(rule, CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);
    }

    private async Task<(Guid, Guid)> AddTwoRevisionRuleAsync(
        Guid instrumentId,
        Instant boundary)
    {
        Guid revisionOneId = Guid.CreateVersion7();
        Guid revisionTwoId = Guid.CreateVersion7();
        MonitoringRule rule = MonitoringRule.Create(
            Guid.CreateVersion7(),
            instrumentId,
            revisionOneId,
            Zone(95m, 100m, 105m),
            T0,
            "synthetic").Value;

        rule.CreateDraft(
            revisionTwoId,
            Zone(96m, 101m, 106m),
            T0.Plus(Duration.FromMinutes(1)),
            "synthetic",
            null,
            null);
        rule.Schedule(
            revisionTwoId,
            boundary,
            null,
            Guid.CreateVersion7(),
            T0.Plus(Duration.FromMinutes(1)));

        await using TradingEngineDbContext context = CreateContext();
        SqlServerMonitoringRuleStore store = new(context, _serializer);
        Result added = await store.AddAsync(rule, CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);
        return (revisionOneId, revisionTwoId);
    }

    private async Task AddObservationAsync(
        Guid instrumentId,
        Instant slot,
        decimal? bid,
        decimal? ask = null)
    {
        ScheduledPriceObservation observation = new(
            instrumentId,
            slot,
            "eToro",
            4242,
            bid,
            ask ?? bid + 1m,
            "USD",
            slot.Plus(Duration.FromSeconds(1)),
            slot.Plus(Duration.FromSeconds(2)),
            MarketQuoteTimeliness.Realtime,
            $"synthetic-{slot.ToUnixTimeSeconds()}",
            Guid.NewGuid());
        PriceCollectionAttempt attempt = new(
            instrumentId,
            slot,
            PriceCollectionOutcome.Observed,
            null,
            "eToro",
            $"synthetic-{slot.ToUnixTimeSeconds()}",
            Guid.NewGuid(),
            slot.Plus(Duration.FromSeconds(2)),
            slot.Plus(Duration.FromSeconds(2)));

        await using TradingEngineDbContext context = CreateContext();
        SqlServerPriceObservationStore store = new(context);
        await store.TryRecordObservationAndAttemptAsync(
            observation,
            attempt,
            CancellationToken.None);
    }

    private async Task RepairObservationAsync(
        Guid instrumentId,
        Instant slot,
        decimal bid)
    {
        await ExecuteNonQueryAsync(
            "UPDATE PriceObservations SET Bid = @bid, Ask = @ask " +
            "WHERE WatchedInstrumentId = @id AND ScheduledFor = @slot",
            ("@id", instrumentId),
            ("@bid", bid),
            ("@ask", bid + 1m),
            ("@slot", slot.ToDateTimeUtc()));
    }

    private int IntentCountFor(Guid instrumentId)
    {
        return ScalarInt(
            "SELECT COUNT(*) FROM NotificationIntents WHERE WatchedInstrumentId = @id",
            instrumentId);
    }

    private int IntentCountAt(Guid instrumentId, Instant slot)
    {
        return ScalarInt(
            "SELECT COUNT(*) FROM NotificationIntents " +
            "WHERE WatchedInstrumentId = @id AND ScheduledFor = @slot",
            instrumentId,
            ("@slot", slot.ToDateTimeUtc()));
    }

    private int IntentStatusCount(Guid instrumentId, string status)
    {
        return ScalarInt(
            "SELECT COUNT(*) FROM NotificationIntents " +
            "WHERE WatchedInstrumentId = @id AND Status = @status",
            instrumentId,
            ("@status", status));
    }

    private int AttemptCountForStatus(Guid instrumentId, string outcome)
    {
        return ScalarInt(
            "SELECT COUNT(*) FROM NotificationAttempts na " +
            "JOIN NotificationIntents ni ON na.NotificationIntentId = ni.Id " +
            "WHERE ni.WatchedInstrumentId = @id AND na.Outcome = @outcome",
            instrumentId,
            ("@outcome", outcome));
    }

    private DateTime? CheckpointFor(Guid instrumentId)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT LastProcessedScheduledFor FROM RecommendationCheckpoints " +
            "WHERE WatchedInstrumentId = @id";
        command.Parameters.AddWithValue("@id", instrumentId);
        return command.ExecuteScalar() as DateTime?;
    }

    private Guid RevisionIdFor(Guid instrumentId, Instant slot)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT MonitoringRuleRevisionId FROM NotificationIntents " +
            "WHERE WatchedInstrumentId = @id AND ScheduledFor = @slot";
        command.Parameters.AddWithValue("@id", instrumentId);
        command.Parameters.AddWithValue("@slot", slot.ToDateTimeUtc());
        return (Guid)command.ExecuteScalar()!;
    }

    private Guid IntentIdFor(Guid instrumentId, Instant slot)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id FROM NotificationIntents " +
            "WHERE WatchedInstrumentId = @id AND ScheduledFor = @slot";
        command.Parameters.AddWithValue("@id", instrumentId);
        command.Parameters.AddWithValue("@slot", slot.ToDateTimeUtc());
        return (Guid)command.ExecuteScalar()!;
    }

    private int ScalarInt(
        string sql,
        Guid instrumentId,
        params (string, object)[] extra)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", instrumentId);
        foreach ((string name, object value) in extra)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (int)command.ExecuteScalar()!;
    }

    private async Task ExecuteNonQueryAsync(
        string sql,
        params (string, object)[] parameters)
    {
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static ChartAnalysisDefinition Zone(decimal lower, decimal level, decimal upper)
    {
        return ChartAnalysisDefinition.Create(
            2,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    lower,
                    level,
                    upper,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("notify").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("notify").Value).Value
                    ]).Value
            ],
            []).Value;
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

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(RecommendationEmail Email, Guid OperationId)> Sends { get; } = [];

        public List<string> Probes { get; } = [];

        public Queue<EmailSendResult> SendResults { get; } = new();

        public Queue<EmailOperationProbe> ProbeResults { get; } = new();

        public Task<EmailSendResult> SendAsync(
            RecommendationEmail email,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            Sends.Add((email, operationId));
            EmailSendResult result = SendResults.Count > 0
                ? SendResults.Dequeue()
                : new EmailSendResult(
                    EmailSendOutcome.Accepted,
                    operationId.ToString("D"),
                    null);

            return Task.FromResult(result);
        }

        public Task<EmailOperationProbe> GetOperationStatusAsync(
            string operationId,
            CancellationToken cancellationToken)
        {
            Probes.Add(operationId);
            EmailOperationProbe probe = ProbeResults.Count > 0
                ? ProbeResults.Dequeue()
                : new EmailOperationProbe(EmailOperationStatus.Unknown, null, null);

            return Task.FromResult(probe);
        }
    }
}
