using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Recommendations;
using TradingEngine.Application.Signals;
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

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        DispatchRunResult resolved = await RunDispatchAsync(sender);

        Guid firstIntentId = IntentIdFor(instrumentId, T0.Plus(Duration.FromMinutes(1)));

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
                dispatch.Items.Count(item => item.Outcome == DispatchItemOutcome.Submitted),
                Is.EqualTo(2));
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(AttemptCountForStatus(instrumentId, "Submitted"), Is.EqualTo(2));

            Assert.That(sender.Probes, Has.Count.EqualTo(2));
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(
                resolved.Items.Count(item => item.Outcome == DispatchItemOutcome.Sent),
                Is.EqualTo(2));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(2));

            CommittedIntent committed = evaluation.Items[0].CommittedIntents
                .Single(item => item.ScheduledFor == T0.Plus(Duration.FromMinutes(1)));
            Assert.That(committed.IntentId, Is.EqualTo(firstIntentId));
            Assert.That(
                committed.MonitoringRuleRevisionId,
                Is.EqualTo(RevisionIdFor(instrumentId, T0.Plus(Duration.FromMinutes(1)))));
            Assert.That(
                committed.CollectionInvocationId,
                Is.EqualTo(
                    ObservationInvocationIdFor(
                        instrumentId,
                        T0.Plus(Duration.FromMinutes(1)))));
            IntentDispatchResult dispatched = dispatch.Items
                .Single(item => item.IntentId == firstIntentId);
            Assert.That(dispatched.InstrumentId, Is.EqualTo(instrumentId));
            Assert.That(dispatched.ScheduledFor, Is.EqualTo(T0.Plus(Duration.FromMinutes(1))));
            Assert.That(
                dispatched.MonitoringRuleRevisionId,
                Is.EqualTo(committed.MonitoringRuleRevisionId));
            Assert.That(
                dispatched.CollectionInvocationId,
                Is.EqualTo(committed.CollectionInvocationId));
        });
    }

    [Test]
    public async Task HandleAsync_MultipleZonesAndConditions_PerConditionClocksRetainEverySignal()
    {
        Guid instrumentId = await InsertInstrumentAsync($"MIX-{_suffix}", "Configured");
        ChartZone lower = Zone(95m, 100m, 105m).SupportZones.Single();
        ChartZone higher = ChartZone.Create(ChartAnalysisIdentifier.From("support-b").Value,
            115m, 120m, 125m, lower.Conditions).Value;
        await AddRuleAsync(instrumentId, ChartAnalysisDefinition.Create(2, [lower, higher], []).Value);
        await AddObservationAsync(instrumentId, T0, 110m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();
        decimal[] bids = [130m, 102m, 110m, 102m, 90m];
        for (int index = 0; index < bids.Length; index++)
        {
            await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(index + 2)), bids[index]);
        }

        await RunEvaluationAsync(suppressRepeats: true);
        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender);
        for (int index = 0; index < sender.Sends.Count; index++)
        {
            sender.ProbeResults.Enqueue(new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        }

        await RunDispatchAsync(sender);
        await RunEvaluationAsync(suppressRepeats: true);
        await RunDispatchAsync(sender);
        await using TradingEngineDbContext context = CreateContext();
        SqlServerSignalEvidenceReader reader = new(context);
        SignalQuery query = new(Instant.FromUtc(2099, 1, 1, 0, 0), instrumentId);
        IReadOnlyList<SignalEvidence> history = await reader.ReadAsync(query, CancellationToken.None);

        (RecommendationEmail Email, Guid OperationId) mixedSend = sender.Sends
            .Single(send => send.OperationId == IntentIdFor(instrumentId, T0.Plus(Duration.FromMinutes(3))));

        Assert.That(sender.Sends, Has.Count.EqualTo(2));
        Assert.That(mixedSend.Email.Subject, Does.Contain("support-loss"));
        Assert.That(mixedSend.Email.PlainTextBody, Does.Contain("support-b"));
        Assert.That(mixedSend.Email.PlainTextBody, Does.Not.Contain("support-a"));
        Assert.That(history.Select(signal => signal.SignalId).Distinct().Count(), Is.EqualTo(5));
        Assert.That(history.Count(signal => signal.EmailStatus == "Sent"), Is.EqualTo(2));
        Assert.That(history.Where(signal => signal.EmailStatus == "SuppressedRepeat")
            .Select(signal => (signal.ZoneId, signal.Condition)),
            Is.EquivalentTo(new[] { ("support-a", "BuyZone"), ("support-a", "BuyZone"),
                ("support-a", "SupportLoss") }));
        Assert.That(IntentStatusCount(instrumentId, "SuppressedRepeat"), Is.EqualTo(2));
    }

    [Test]
    public async Task HandleAsync_PerConditionCooldowns_RestartPerConditionAndKeepEachAssetIndependent()
    {
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson("""{"defaultPeriod":"PT2H"}""");
        Guid firstAsset = await InsertInstrumentAsync($"CLK-{_suffix}", "Configured");
        Guid secondAsset = await InsertInstrumentAsync($"OWN-{_suffix}", "Configured");
        foreach (Guid asset in new[] { firstAsset, secondAsset })
        {
            await AddRuleAsync(asset, Zone(95m, 100m, 105m));
            await AddObservationAsync(asset, T0, 110m);
        }

        await RunEvaluationAsync(policy: policy);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(1), 102m);
        await RunEvaluationAsync(policy: policy);
        RecordingEmailSender sender = new();
        Instant firstAlert = T0 + Duration.FromMinutes(20);
        BatchTestClock clock = new() { Now = firstAlert };
        await RunDispatchAsync(sender, clock);
        sender.ProbeResults.Enqueue(new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        await RunDispatchAsync(sender, clock);

        Instant boundary = firstAlert + Duration.FromHours(2);
        await AddObservationAsync(firstAsset, boundary - Duration.FromMinutes(2), 90m);
        await AddObservationAsync(secondAsset, boundary - Duration.FromMinutes(2), 90m);
        await RunEvaluationAsync(policy: policy);
        clock.Now = boundary - Duration.FromMinutes(1);
        await RunDispatchAsync(sender, clock);
        Assert.That(sender.Sends.Select(send => send.OperationId), Is.EquivalentTo(new[]
        {
            IntentIdFor(firstAsset, T0 + Duration.FromMinutes(1)),
            IntentIdFor(firstAsset, boundary - Duration.FromMinutes(2)),
            IntentIdFor(secondAsset, boundary - Duration.FromMinutes(2))
        }));

        await AddObservationAsync(firstAsset, boundary - Duration.FromMinutes(1), 105m);
        await AddObservationAsync(firstAsset, boundary, 90m);
        await RunEvaluationAsync(policy: policy);
        clock.Now = boundary;
        await RunDispatchAsync(sender, clock);
        Assert.That(sender.Sends, Has.Count.EqualTo(3));
        Assert.That(sender.Sends.Last().Email.Subject, Does.Contain("support-loss"));

        await AddObservationAsync(firstAsset, boundary + Duration.FromMinutes(1), 110m);
        await AddObservationAsync(firstAsset, boundary + Duration.FromMinutes(2), 102m);
        await AddObservationAsync(firstAsset, boundary + Duration.FromMinutes(3), 110m);
        await AddObservationAsync(firstAsset, boundary + Duration.FromHours(2), 102m);
        await RunEvaluationAsync(policy: policy);
        clock.Now = boundary + Duration.FromHours(2);
        await RunDispatchAsync(sender, clock);

        Assert.That(sender.Sends, Has.Count.EqualTo(4));
        Assert.That(sender.Sends.Last().Email.Subject, Does.Contain("buy-zone entry"));
        Assert.That(IntentStatusCount(firstAsset, "SuppressedRepeat"), Is.EqualTo(2));
        Assert.That(IntentCountFor(firstAsset), Is.EqualTo(5));
        Assert.That(IntentCountFor(secondAsset), Is.EqualTo(1));
    }

    [Test]
    public async Task HandleAsync_BuyZoneSent_SupportLossWithinCooldown_StillEmails()
    {
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson("""{"defaultPeriod":"P2D"}""");
        Guid asset = await InsertInstrumentAsync($"BZSL-{_suffix}", "Configured");
        await AddRuleAsync(asset, Zone(95m, 100m, 105m));
        await AddObservationAsync(asset, T0, 110m);
        await RunEvaluationAsync(policy: policy);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync(policy: policy);

        RecordingEmailSender sender = new();
        BatchTestClock clock = new() { Now = T0.Plus(Duration.FromMinutes(10)) };
        await RunDispatchAsync(sender, clock);

        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(2)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(3)), 90m);
        await RunEvaluationAsync(policy: policy);
        await RunDispatchAsync(sender, clock);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(sender.Sends[0].Email.Subject, Does.Contain("buy-zone entry"));
            Assert.That(sender.Sends[1].Email.Subject, Does.Contain("support-loss"));
            Assert.That(IntentStatusCount(asset, "SuppressedRepeat"), Is.EqualTo(0));
            Assert.That(IntentCountFor(asset), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_SupportLossSent_BuyZoneWithinCooldown_StillEmails()
    {
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson("""{"defaultPeriod":"P2D"}""");
        Guid asset = await InsertInstrumentAsync($"SLBZ-{_suffix}", "Configured");
        await AddRuleAsync(asset, Zone(95m, 100m, 105m));
        await AddObservationAsync(asset, T0, 110m);
        await RunEvaluationAsync(policy: policy);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(1)), 90m);
        await RunEvaluationAsync(policy: policy);

        RecordingEmailSender sender = new();
        BatchTestClock clock = new() { Now = T0.Plus(Duration.FromMinutes(10)) };
        await RunDispatchAsync(sender, clock);

        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(2)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(3)), 102m);
        await RunEvaluationAsync(policy: policy);
        await RunDispatchAsync(sender, clock);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(sender.Sends[0].Email.Subject, Does.Contain("support-loss"));
            Assert.That(sender.Sends[1].Email.Subject, Does.Contain("buy-zone entry"));
            Assert.That(IntentStatusCount(asset, "SuppressedRepeat"), Is.EqualTo(0));
            Assert.That(IntentCountFor(asset), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_PerConditionCooldowns_ExpireIndependently()
    {
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson("""{"defaultPeriod":"PT2H"}""");
        Guid asset = await InsertInstrumentAsync($"EXP-{_suffix}", "Configured");
        await AddRuleAsync(asset, Zone(95m, 100m, 105m));
        await AddObservationAsync(asset, T0, 110m);
        await RunEvaluationAsync(policy: policy);

        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync(policy: policy);
        RecordingEmailSender sender = new();
        BatchTestClock clock = new() { Now = T0.Plus(Duration.FromMinutes(10)) };
        await RunDispatchAsync(sender, clock);

        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(2)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(3)), 90m);
        await RunEvaluationAsync(policy: policy);
        clock.Now = T0.Plus(Duration.FromMinutes(15));
        await RunDispatchAsync(sender, clock);

        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(120)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(130)), 102m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(131)), 90m);
        await RunEvaluationAsync(policy: policy);
        clock.Now = T0.Plus(Duration.FromMinutes(131));
        await RunDispatchAsync(sender, clock);

        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(140)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(141)), 90m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(144)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(145)), 102m);
        await RunEvaluationAsync(policy: policy);
        clock.Now = T0.Plus(Duration.FromMinutes(141));
        await RunDispatchAsync(sender, clock);

        await using TradingEngineDbContext context = CreateContext();
        SqlServerSignalEvidenceReader reader = new(context);
        SignalQuery query = new(Instant.FromUtc(2099, 1, 1, 0, 0), asset);
        IReadOnlyList<SignalEvidence> history = await reader.ReadAsync(query, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(4));
            Assert.That(sender.Sends[0].Email.Subject, Does.Contain("buy-zone entry"));
            Assert.That(sender.Sends[1].Email.Subject, Does.Contain("support-loss"));
            Assert.That(sender.Sends[2].Email.Subject, Does.Contain("buy-zone entry"));
            Assert.That(sender.Sends[3].Email.Subject, Does.Contain("support-loss"));
            Assert.That(IntentStatusCount(asset, "SuppressedRepeat"), Is.EqualTo(2));
            Assert.That(IntentCountFor(asset), Is.EqualTo(6));
            Assert.That(history.Count(signal => signal.EmailStatus == "SuppressedRepeat"), Is.EqualTo(2));
            Assert.That(history.Count(signal => signal.EmailStatus != "SuppressedRepeat"), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task HandleAsync_OutstandingSubmission_ReservesOnlyItsOwnCondition()
    {
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson("""{"defaultPeriod":"P2D"}""");
        Guid asset = await InsertInstrumentAsync($"RES-{_suffix}", "Configured");
        await AddRuleAsync(asset, Zone(95m, 100m, 105m));
        await AddObservationAsync(asset, T0, 110m);
        await RunEvaluationAsync(policy: policy);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(1)), 102m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(2)), 90m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(3)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(4)), 102m);
        await RunEvaluationAsync(policy: policy);

        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(new EmailSendResult(EmailSendOutcome.TerminalFailure, null, "synthetic"));
        sender.SendResults.Enqueue(new EmailSendResult(EmailSendOutcome.Ambiguous, null, "timeout"));
        await RunDispatchAsync(sender);

        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(5)), 110m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(6)), 102m);
        await AddObservationAsync(asset, T0.Plus(Duration.FromMinutes(7)), 90m);
        await RunEvaluationAsync(policy: policy);
        sender.ProbeResults.Enqueue(new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(3));
            Assert.That(
                sender.Sends[0].OperationId,
                Is.EqualTo(IntentIdFor(asset, T0.Plus(Duration.FromMinutes(1)))));
            Assert.That(
                sender.Sends[1].OperationId,
                Is.EqualTo(IntentIdFor(asset, T0.Plus(Duration.FromMinutes(2)))));
            Assert.That(
                sender.Sends[2].OperationId,
                Is.EqualTo(IntentIdFor(asset, T0.Plus(Duration.FromMinutes(6)))));
            Assert.That(IntentStatusCount(asset, "Failed"), Is.EqualTo(1));
            Assert.That(IntentStatusCount(asset, "SuppressedRepeat"), Is.EqualTo(2));
            Assert.That(IntentStatusCount(asset, "Sent"), Is.EqualTo(1));
            Assert.That(IntentStatusCount(asset, "Submitted"), Is.EqualTo(1));
            Assert.That(IntentCountFor(asset), Is.EqualTo(5));
        });
    }

    [TestCase(ChartConditionType.BuyZone, ChartConditionType.SupportLoss, false)]
    [TestCase(ChartConditionType.SupportLoss, ChartConditionType.BuyZone, false)]
    [TestCase(ChartConditionType.BuyZone, ChartConditionType.ResistanceEntry, false)]
    [TestCase(ChartConditionType.ResistanceEntry, ChartConditionType.BuyZone, false)]
    [TestCase(ChartConditionType.SupportLoss, ChartConditionType.ResistanceEntry, false)]
    [TestCase(ChartConditionType.ResistanceEntry, ChartConditionType.SupportLoss, false)]
    [TestCase(ChartConditionType.BuyZone, ChartConditionType.SupportLoss, true)]
    [TestCase(ChartConditionType.SupportLoss, ChartConditionType.BuyZone, true)]
    [TestCase(ChartConditionType.BuyZone, ChartConditionType.ResistanceEntry, true)]
    [TestCase(ChartConditionType.ResistanceEntry, ChartConditionType.BuyZone, true)]
    [TestCase(ChartConditionType.SupportLoss, ChartConditionType.ResistanceEntry, true)]
    [TestCase(ChartConditionType.ResistanceEntry, ChartConditionType.SupportLoss, true)]
    public async Task HandleAsync_DistinctConditions_OnlySuppressesSameCondition(
        ChartConditionType first, ChartConditionType second, bool accepted)
    {
        Guid asset = await InsertInstrumentAsync($"PAIR-{_suffix}", "Configured");
        await AddRuleAsync(asset, ResistanceDefinition());
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson("""{"defaultPeriod":"PT2H"}""");
        RecordingEmailSender sender = new();
        BatchTestClock clock = new() { Now = T0 + Duration.FromMinutes(10) };
        await AddObservationAsync(asset, T0, 110m);
        await RunEvaluationAsync(policy: policy, clock: clock);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(1), TriggerPrice(first));
        await RunEvaluationAsync(policy: policy, clock: clock);
        if (accepted)
        {
            await RunDispatchAsync(sender, clock);
            sender.ProbeResults.Enqueue(new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
            await RunDispatchAsync(sender, clock);
        }

        await AddObservationAsync(asset, T0 + Duration.FromMinutes(2), 110m);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(3), TriggerPrice(second));
        await RunEvaluationAsync(policy: policy, clock: clock);
        await RunDispatchAsync(sender, clock);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(4), 110m);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(5), TriggerPrice(first));
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(6), 110m);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(7), TriggerPrice(second));
        await RunEvaluationAsync(policy: policy, clock: clock);
        await RunEvaluationAsync(policy: policy, clock: clock);
        await RunDispatchAsync(sender, clock);
        await using TradingEngineDbContext context = CreateContext();
        IReadOnlyList<SignalEvidence> history = await new SqlServerSignalEvidenceReader(context).ReadAsync(
            new SignalQuery(T0 + Duration.FromHours(1), asset), CancellationToken.None);

        Assert.That(sender.Sends.Select(send => send.OperationId), Is.EquivalentTo(new[]
        {
            IntentIdFor(asset, T0 + Duration.FromMinutes(1)),
            IntentIdFor(asset, T0 + Duration.FromMinutes(3))
        }));
        Assert.That(history, Has.Count.EqualTo(4));
        Assert.That(history.Where(signal => signal.EmailStatus == "SuppressedRepeat").Select(signal => signal.Condition),
            Is.EquivalentTo(new[] { first.ToString(), second.ToString() }));
    }

    [Test]
    public async Task HandleAsync_ResistanceEntryCooldown_ExpiresWithoutSuppressedEventsExtendingIt()
    {
        Guid firstAsset = await InsertInstrumentAsync($"ENT-{_suffix}", "Configured");
        Guid secondAsset = await InsertInstrumentAsync($"OTH-{_suffix}", "Configured");
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson("""{"defaultPeriod":"PT2H"}""");
        foreach (Guid asset in new[] { firstAsset, secondAsset })
        {
            await AddRuleAsync(asset, ResistanceDefinition());
            await AddObservationAsync(asset, T0, 110m);
        }

        BatchTestClock clock = new() { Now = T0 + Duration.FromMinutes(10) };
        await RunEvaluationAsync(policy: policy, clock: clock);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(1), 125m);
        await RunEvaluationAsync(policy: policy, clock: clock);
        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender, clock);
        sender.ProbeResults.Enqueue(new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        await RunDispatchAsync(sender, clock);

        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(20), 102m);
        clock.Now = T0 + Duration.FromMinutes(30);
        await RunEvaluationAsync(policy: policy, clock: clock);
        await RunDispatchAsync(sender, clock);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(128), 110m);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(129), 125m);
        await AddObservationAsync(secondAsset, T0 + Duration.FromMinutes(129), 125m);
        clock.Now = T0 + Duration.FromMinutes(129);
        await RunEvaluationAsync(policy: policy, clock: clock);
        await RunDispatchAsync(sender, clock);

        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(129) + Duration.FromSeconds(30), 110m);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(130), 125m);
        clock.Now = T0 + Duration.FromMinutes(130);
        await RunEvaluationAsync(policy: policy, clock: clock);
        await RunDispatchAsync(sender, clock);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(131), 102m);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(132), 110m);
        await AddObservationAsync(firstAsset, T0 + Duration.FromMinutes(133), 125m);
        clock.Now = T0 + Duration.FromMinutes(133);
        await RunEvaluationAsync(policy: policy, clock: clock);
        await RunDispatchAsync(sender, clock);

        Assert.That(sender.Sends, Has.Count.EqualTo(4));
        Assert.That(sender.Sends.Count(send => send.Email.Subject.Contains("resistance entry", StringComparison.Ordinal)),
            Is.EqualTo(3));
        Assert.That(IntentStatusCount(firstAsset, "SuppressedRepeat"), Is.EqualTo(3));
        Assert.That(IntentStatusCount(secondAsset, "SuppressedRepeat"), Is.Zero);
        Assert.That(sender.Sends.Last().OperationId, Is.EqualTo(IntentIdFor(firstAsset, T0 + Duration.FromMinutes(130))));
    }

    [Test]
    public async Task HandleAsync_ResistanceEntryAcrossZonesAndRevisions_SharesClockAndPreservesEvidence()
    {
        Guid asset = await InsertInstrumentAsync($"REVRE-{_suffix}", "Configured");
        Guid originalRevision = Guid.NewGuid();
        Guid newRevision = Guid.NewGuid();
        MonitoringRule rule = MonitoringRule.Create(Guid.NewGuid(), asset, originalRevision,
            ResistanceDefinition(), T0, "synthetic").Value;
        rule.CreateDraft(newRevision, ResistanceDefinition("resistance-b"), T0 + Duration.FromSeconds(1),
            "synthetic", null, null);
        rule.Schedule(newRevision, T0 + Duration.FromMinutes(3), null, Guid.NewGuid(), T0 + Duration.FromSeconds(1));
        await using (TradingEngineDbContext setup = CreateContext())
        {
            Result added = await new SqlServerMonitoringRuleStore(setup, _serializer).AddAsync(rule, CancellationToken.None);
            Assert.That(added.IsSuccess, Is.True);
        }

        BatchTestClock clock = new() { Now = T0 + Duration.FromMinutes(10) };
        await AddObservationAsync(asset, T0, 110m);
        await RunEvaluationAsync(suppressRepeats: true, clock: clock);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(1), 125m);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(2), 110m);
        await AddObservationAsync(asset, T0 + Duration.FromMinutes(3), 125m);
        await RunEvaluationAsync(suppressRepeats: true, clock: clock);
        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender, clock);
        await using TradingEngineDbContext context = CreateContext();
        IReadOnlyList<SignalEvidence> history = await new SqlServerSignalEvidenceReader(context).ReadAsync(
            new SignalQuery(T0 + Duration.FromHours(1), asset), CancellationToken.None);

        Assert.That(sender.Sends, Has.Count.EqualTo(1));
        Assert.That(history.Select(signal => (signal.ZoneId, signal.RevisionId, signal.EmailStatus)), Is.EquivalentTo(new[]
        {
            ("resistance-a", originalRevision, "Submitted"),
            ("resistance-b", newRevision, "SuppressedRepeat")
        }));
    }

    private static ChartAnalysisDefinition ResistanceDefinition(string zoneId = "resistance-a")
    {
        ChartAnalysisIdentifier action = ChartAnalysisIdentifier.From("publish-signal").Value;
        ChartZone resistance = ChartZone.Create(ChartAnalysisIdentifier.From(zoneId).Value, 120m, 125m, 130m,
            [ChartCondition.Create(ChartConditionType.Breakout, action).Value,
             ChartCondition.Create(ChartConditionType.ResistanceEntry, action).Value]).Value;
        return ChartAnalysisDefinition.Create(2, Zone(95m, 100m, 105m).SupportZones, [resistance]).Value;
    }

    private static decimal TriggerPrice(ChartConditionType condition)
    {
        return condition switch
        {
            ChartConditionType.BuyZone => 102m,
            ChartConditionType.SupportLoss => 90m,
            ChartConditionType.ResistanceEntry => 125m,
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
    }

    [Test]
    public async Task HandleAsync_BreakoutCrossings_SendIndependentlyOfCoveredConditions()
    {
        Guid asset = await InsertInstrumentAsync($"BRK-{_suffix}", "Configured");
        ChartZone support = Zone(95m, 100m, 105m).SupportZones.Single();
        ChartCondition breakout = ChartCondition.Create(ChartConditionType.Breakout,
            ChartAnalysisIdentifier.From("notify").Value).Value;
        ChartZone resistance = ChartZone.Create(ChartAnalysisIdentifier.From("resistance").Value,
            115m, 120m, 125m, [breakout]).Value;
        await AddRuleAsync(asset, ChartAnalysisDefinition.Create(2, [support], [resistance]).Value);
        await AddObservationAsync(asset, T0, 110m);
        await RunEvaluationAsync(suppressRepeats: true);
        decimal[] bids = [130m, 102m, 130m, 90m, 130m, 110m, 90m];
        for (int index = 0; index < bids.Length; index++)
        {
            await AddObservationAsync(asset, T0 + Duration.FromMinutes(index + 1), bids[index]);
        }

        await RunEvaluationAsync(suppressRepeats: true);
        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender);

        Assert.That(sender.Sends, Has.Count.EqualTo(5));
        Assert.That(sender.Sends.Count(send => send.Email.Subject.Contains("breakout", StringComparison.Ordinal)), Is.EqualTo(3));
        Assert.That(sender.Sends.Count(send => send.Email.Subject.Contains("buy-zone", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(sender.Sends.Count(send => send.Email.Subject.Contains("support-loss", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(IntentStatusCount(asset, "SuppressedRepeat"), Is.EqualTo(1));
    }

    [Test]
    public async Task HandleAsync_PriorSelectionFailed_LaterCrossingCanSend()
    {
        Guid instrumentId = await InsertInstrumentAsync($"FAIL-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 110m);
        await RunEvaluationAsync(suppressRepeats: true);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync(suppressRepeats: true);
        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(new EmailSendResult(EmailSendOutcome.TerminalFailure, null, "synthetic"));
        await RunDispatchAsync(sender);

        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), 110m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(3)), 102m);
        await RunEvaluationAsync(suppressRepeats: true);
        await RunDispatchAsync(sender);

        Assert.That(sender.Sends, Has.Count.EqualTo(2));
        Assert.That(IntentStatusCount(instrumentId, "Failed"), Is.EqualTo(1));
        Assert.That(IntentStatusCount(instrumentId, "Submitted"), Is.EqualTo(1));
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
        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
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

    [TestCase(false, 2, false)]
    [TestCase(true, 1, false)]
    [TestCase(false, 2, true)]
    [TestCase(true, 1, true)]
    public async Task HandleAsync_WhenWorkersRace_SingleIntentPerTrigger(
        bool suppressRepeats, int expectedSends, bool resistanceEntry)
    {
        Guid instrumentId = await InsertInstrumentAsync($"RCW-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, resistanceEntry ? ResistanceDefinition() : Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, resistanceEntry ? 110m : 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), resistanceEntry ? 125m : 102m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), resistanceEntry ? 126m : 97m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(3)), 110m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(4)), resistanceEntry ? 125m : 102m);

        await using TradingEngineDbContext contextA = CreateContext();
        await using TradingEngineDbContext contextB = CreateContext();

        Task<EvaluationRunResult> first = CreateHandler(contextA, suppressRepeats).HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);
        Task<EvaluationRunResult> second = CreateHandler(contextB, suppressRepeats).HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);

        await Task.WhenAll(first, second);

        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender);
        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        await RunDispatchAsync(sender);
        await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(2));
            Assert.That(sender.Sends, Has.Count.EqualTo(expectedSends));
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
        await RunEvaluationAsync(suppressRepeats: true);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(2)), 110m);
        await AddObservationAsync(instrumentId, boundary, 120m);
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(4)), 102m);
        await RunEvaluationAsync(suppressRepeats: true);

        Guid first = RevisionIdFor(instrumentId, T0.Plus(Duration.FromMinutes(1)));
        Guid second = RevisionIdFor(instrumentId, T0.Plus(Duration.FromMinutes(4)));

        Assert.Multiple(() =>
        {
            Assert.That(IntentCountFor(instrumentId), Is.EqualTo(2));
            Assert.That(IntentStatusCount(instrumentId, "Pending"), Is.EqualTo(1));
            Assert.That(IntentStatusCount(instrumentId, "SuppressedRepeat"), Is.EqualTo(1));
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
                    Guid.CreateVersion7(),
                    instrumentId,
                    slot,
                    T0,
                    PhantomRevisionId,
                    PriceBasis.Bid,
                    "USD",
                    100m,
                    101m,
                    [],
                    null)),
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

        NotificationClaim? claimedFirst;
        NotificationClaim? claimedSecond;
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

        NotificationClaim? claimedAfterExpiry;
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
            Assert.That(claimedFirst, Is.Not.Null);
            Assert.That(claimedFirst!.Status, Is.EqualTo(NotificationStatus.Pending));
            Assert.That(claimedSecond, Is.Null);
            Assert.That(claimedAfterExpiry, Is.Not.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenOutcomeLostAfterAcceptance_ProbesOnRecovery()
    {
        Guid instrumentId = await InsertInstrumentAsync($"LOST-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        await using (TradingEngineDbContext context = CreateContext())
        {
            DispatchPendingNotificationsHandler handler = new(
                new SuppressingOutcomeStore(context),
                sender,
                new RecommendationEmailRenderer(),
                SystemClock.Instance,
                new NotificationOptions { MaxDispatchAttempts = 3 });
            DispatchRunResult crashed = await handler.HandleAsync(
                new DispatchPendingNotifications(),
                CancellationToken.None);
            Assert.That(
                crashed.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.ClaimLost));
        }

        await ExecuteNonQueryAsync(
            "UPDATE NotificationIntents SET ClaimedUntil = '2000-01-01T00:00:00.0000000' " +
            "WHERE WatchedInstrumentId = @id",
            ("@id", instrumentId));

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        DispatchRunResult recovered = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(1));
            Assert.That(sender.Probes, Has.Count.EqualTo(1));
            Assert.That(
                recovered.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TryClaimAsync_WhenCandidateWentAmbiguous_ReturnsCurrentState()
    {
        Guid instrumentId = await InsertInstrumentAsync($"STALE-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.Ambiguous, null, "timeout"));
        await RunDispatchAsync(sender);

        Guid intentId = IntentIdFor(instrumentId, T0.Plus(Duration.FromMinutes(1)));
        Instant now = Instant.FromUtc(2026, 10, 30, 13, 50);

        NotificationClaim? claim;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerNotificationIntentStore store = new(context);
            claim = await store.TryClaimAsync(
                intentId,
                Guid.NewGuid(),
                now.Plus(Duration.FromSeconds(120)),
                now,
                CancellationToken.None);
        }

        Assert.Multiple(() =>
        {
            Assert.That(claim, Is.Not.Null);
            Assert.That(claim!.Status, Is.EqualTo(NotificationStatus.Ambiguous));
            Assert.That(
                claim.ProviderOperationId,
                Is.EqualTo(intentId.ToString("D")));
        });
    }

    [Test]
    public async Task HandleAsync_WhenOperationRunningAfterAttemptLimit_StaysAmbiguous()
    {
        Guid instrumentId = await InsertInstrumentAsync($"RUN-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(
            new EmailSendResult(
                EmailSendOutcome.TransientFailure, null, "http_429"));
        sender.SendResults.Enqueue(
            new EmailSendResult(
                EmailSendOutcome.TransientFailure, null, "http_429"));
        sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.Ambiguous, null, "timeout"));
        await RunDispatchAsync(sender);
        await RunDispatchAsync(sender);
        await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.InFlight, "Running", null));
        DispatchRunResult limited = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(
                limited.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.InFlight));
            Assert.That(sender.Sends, Has.Count.EqualTo(3));
            Assert.That(IntentStatusCount(instrumentId, "Submitted"), Is.EqualTo(1));
            Assert.That(IntentStatusCount(instrumentId, "Ambiguous"), Is.EqualTo(0));
            Assert.That(IntentStatusCount(instrumentId, "Failed"), Is.EqualTo(0));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Suppressed"),
                Is.EqualTo(0));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Unresolved"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenTimeoutThenRunningThenNotFoundThenSucceeded_SendsOnce()
    {
        Guid instrumentId = await InsertInstrumentAsync($"TMO-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.Ambiguous, null, "timeout"));
        await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.InFlight, "Running", null));
        DispatchRunResult running = await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));
        DispatchRunResult lost = await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        DispatchRunResult recovered = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(1));
            Assert.That(sender.Probes, Has.Count.EqualTo(3));
            Assert.That(
                running.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.InFlight));
            Assert.That(
                lost.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(
                recovered.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(1));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Submitted"),
                Is.EqualTo(0));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Unresolved"),
                Is.EqualTo(2));
            Assert.That(
                AttemptCountForStatus(instrumentId, "ResolvedSucceeded"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenAcceptedThenNotFoundThenRecovered_SendsOnce()
    {
        Guid instrumentId = await InsertInstrumentAsync($"NOT-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));
        DispatchRunResult lost = await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        DispatchRunResult recovered = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(1));
            Assert.That(sender.Probes, Has.Count.EqualTo(2));
            Assert.That(
                lost.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(
                recovered.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(1));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Unresolved"),
                Is.EqualTo(1));
            Assert.That(
                AttemptCountForStatus(instrumentId, "ResolvedSucceeded"),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenTimeoutThenNotFoundsThenRestartThenSucceeded_SendsOnce()
    {
        Guid instrumentId = await InsertInstrumentAsync($"REX-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.Ambiguous, null, "timeout"));
        await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));
        DispatchRunResult lostFirst = await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));
        DispatchRunResult lostSecond = await RunDispatchAsync(sender);

        sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        DispatchRunResult recovered = await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(sender.Sends, Has.Count.EqualTo(1));
            Assert.That(sender.Probes, Has.Count.EqualTo(3));
            Assert.That(
                lostFirst.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(
                lostSecond.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(
                recovered.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(IntentStatusCount(instrumentId, "Sent"), Is.EqualTo(1));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Unresolved"),
                Is.EqualTo(2));
        });
    }

    [Test]
    public async Task HandleAsync_WhenOperationPollsThreeTimes_RecordsZeroFailures()
    {
        Guid instrumentId = await InsertInstrumentAsync($"POLL-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender sender = new();
        await RunDispatchAsync(sender);

        DispatchRunResult[] polls = new DispatchRunResult[3];
        for (int index = 0; index < 3; index++)
        {
            sender.ProbeResults.Enqueue(
                new EmailOperationProbe(
                    EmailOperationStatus.InFlight, "Running", null));
            polls[index] = await RunDispatchAsync(sender);
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                polls.Select(poll => poll.Items[0].Outcome),
                Is.All.EqualTo(DispatchItemOutcome.InFlight));
            Assert.That(sender.Sends, Has.Count.EqualTo(1));
            Assert.That(sender.Probes, Has.Count.EqualTo(3));
            Assert.That(IntentStatusCount(instrumentId, "Submitted"), Is.EqualTo(1));
            Assert.That(IntentStatusCount(instrumentId, "Failed"), Is.EqualTo(0));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Unresolved"),
                Is.EqualTo(3));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Failed"),
                Is.EqualTo(0));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Ambiguous"),
                Is.EqualTo(0));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Suppressed"),
                Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleAsync_WhenUnresolvedPrecedeFreshIntent_SubmitsNewIntentFirst()
    {
        Guid instrumentId = await InsertInstrumentAsync($"FAIR-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        Guid freshIntentId = IntentIdFor(
            instrumentId,
            T0.Plus(Duration.FromMinutes(1)));
        List<Guid> olderIntentIds = new(50);
        for (int index = 0; index < 50; index++)
        {
            olderIntentIds.Add(await SeedUnresolvedIntentAsync(instrumentId, index));
        }

        RecordingEmailSender sender = new();
        sender.SendResults.Enqueue(
            new EmailSendResult(
                EmailSendOutcome.TransientFailure, null, "http_429"));
        for (int index = 0; index < 160; index++)
        {
            sender.ProbeResults.Enqueue(
                new EmailOperationProbe(
                    EmailOperationStatus.InFlight, "Running", null));
        }

        DispatchRunResult first = await RunDispatchAsync(sender);
        DispatchRunResult second = await RunDispatchAsync(sender);
        await RunDispatchAsync(sender);

        Assert.Multiple(() =>
        {
            Assert.That(
                first.Items[0].IntentId,
                Is.EqualTo(freshIntentId));
            Assert.That(sender.Sends, Has.Count.EqualTo(2));
            Assert.That(
                sender.Sends[0].OperationId,
                Is.EqualTo(freshIntentId));
            Assert.That(
                sender.Sends[1].OperationId,
                Is.EqualTo(freshIntentId));
            Assert.That(
                sender.Probes,
                Contains.Item(freshIntentId.ToString("D")));
            foreach (Guid olderId in olderIntentIds)
            {
                Assert.That(
                    sender.Probes,
                    Contains.Item(olderId.ToString("D")));
            }

            Assert.That(
                first.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.RetryPending));
            Assert.That(
                second.Items.Count(
                    item => item.Outcome == DispatchItemOutcome.InFlight),
                Is.EqualTo(49));
        });
    }

    [Test]
    public async Task HandleAsync_WhenWorkersShareStaleCandidate_EnforcesAttemptLimitUnderClaim()
    {
        Guid instrumentId = await InsertInstrumentAsync($"LMT-{_suffix}", "Configured");
        await AddRuleAsync(instrumentId, Zone(95m, 100m, 105m));
        await AddObservationAsync(instrumentId, T0, 120m);
        await RunEvaluationAsync();
        await AddObservationAsync(instrumentId, T0.Plus(Duration.FromMinutes(1)), 102m);
        await RunEvaluationAsync();

        RecordingEmailSender workerA = new();
        for (int index = 0; index < 3; index++)
        {
            workerA.SendResults.Enqueue(
                new EmailSendResult(
                    EmailSendOutcome.TransientFailure, null, "http_429"));
            await RunDispatchAsync(workerA);
        }

        Guid intentId = IntentIdFor(
            instrumentId,
            T0.Plus(Duration.FromMinutes(1)));

        BeginSubmissionResult staleClaim;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerNotificationIntentStore store = new(context);
            Guid claimToken = Guid.NewGuid();
            NotificationClaim? claim = await store.TryClaimAsync(
                intentId,
                claimToken,
                Instant.FromUtc(2026, 10, 30, 14, 0, 0),
                Instant.FromUtc(2026, 10, 30, 13, 58, 0),
                CancellationToken.None);
            Assert.That(claim, Is.Not.Null);
            staleClaim = await store.TryBeginSubmissionAsync(
                intentId,
                claimToken,
                intentId.ToString("D"),
                3,
                Instant.FromUtc(2026, 10, 30, 13, 58, 0),
                CancellationToken.None);
        }

        await ExecuteNonQueryAsync(
            "UPDATE NotificationIntents SET ClaimToken = NULL, " +
            "ClaimedUntil = '2000-01-01T00:00:00.0000000' WHERE Id = @id",
            ("@id", intentId));

        RecordingEmailSender workerB = new();
        DispatchRunResult second = await RunDispatchAsync(workerB);

        Assert.Multiple(() =>
        {
            Assert.That(workerA.Sends, Has.Count.EqualTo(3));
            Assert.That(workerB.Sends, Is.Empty);
            Assert.That(
                second.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Suppressed));
            Assert.That(
                staleClaim,
                Is.EqualTo(BeginSubmissionResult.AttemptsExhausted));
            Assert.That(IntentStatusCount(instrumentId, "Failed"), Is.EqualTo(1));
            Assert.That(
                AttemptCountForStatus(instrumentId, "Suppressed"),
                Is.EqualTo(1));
        });
    }

    private async Task<Guid> SeedUnresolvedIntentAsync(
        Guid instrumentId,
        int index)
    {
        Guid intentId = Guid.NewGuid();
        Guid revisionId = RevisionIdFor(
            instrumentId,
            T0.Plus(Duration.FromMinutes(1)));
        await ExecuteNonQueryAsync(
            "INSERT INTO NotificationIntents " +
            "(Id, WatchedInstrumentId, ScheduledFor, MonitoringRuleRevisionId, " +
            "Reason, PriceBasis, QuoteCurrency, Status, ProviderOperationId, " +
            "CreatedAt, UpdatedAt) " +
            "VALUES (@intentId, @instrumentId, @slot, @rev, " +
            "'ConditionsTriggered', 'Bid', 'USD', 'Submitted', @operationId, " +
            "'2026-10-30T14:00:00', '2026-10-30T14:00:00'); " +
            "INSERT INTO NotificationAttempts " +
            "(Id, NotificationIntentId, AttemptedAt, Outcome, " +
            "ProviderOperationId, RecordedAt) " +
            "VALUES (NEWID(), @intentId, '2026-10-30T14:00:00', 'Submitted', " +
            "@operationId, '2026-10-30T14:00:00')",
            ("@intentId", intentId),
            ("@instrumentId", instrumentId),
            ("@slot", T0.Plus(Duration.FromMinutes(index + 10)).ToDateTimeUtc()),
            ("@rev", revisionId),
            ("@operationId", intentId.ToString("D")));
        return intentId;
    }

    private async Task<EvaluationRunResult> RunEvaluationAsync(bool suppressRepeats = false, AssetEmailCooldown? policy = null, IClock? clock = null)
    {
        await using TradingEngineDbContext context = CreateContext();
        return await CreateHandler(context, suppressRepeats, policy, clock).HandleAsync(
            new EvaluateSavedPrices(),
            CancellationToken.None);
    }

    private EvaluateSavedPricesHandler CreateHandler(TradingEngineDbContext context, bool suppressRepeats = false, AssetEmailCooldown? policy = null, IClock? clock = null)
    {
        return new EvaluateSavedPricesHandler(
            new SqlServerRecommendationStore(context, policy ?? (suppressRepeats
                ? AssetEmailCooldown.FromJson("""{"defaultPeriod":"P2D"}""")
                : AssetEmailCooldown.Disabled)),
            new SqlServerMonitoringRuleStore(context, _serializer),
            new ChartAnalysisEvaluator(),
            clock ?? SystemClock.Instance,
            TimeProvider.System,
            new RecommendationOptions
            {
                PriceBasis = "Bid",
                MaxExecutionTimeSeconds = 60
            });
    }

    private async Task<DispatchRunResult> RunDispatchAsync(RecordingEmailSender sender, IClock? clock = null)
    {
        await using TradingEngineDbContext context = CreateContext();
        DispatchPendingNotificationsHandler handler = new(
            new SqlServerNotificationIntentStore(context),
            sender,
            new RecommendationEmailRenderer(),
            clock ?? SystemClock.Instance,
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

    private Guid? ObservationInvocationIdFor(Guid instrumentId, Instant slot)
    {
        using SqlConnection connection = new(ConnectionStringFor());
        connection.Open();
        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT InvocationId FROM PriceObservations " +
            "WHERE WatchedInstrumentId = @id AND ScheduledFor = @slot";
        command.Parameters.AddWithValue("@id", instrumentId);
        command.Parameters.AddWithValue("@slot", slot.ToDateTimeUtc());
        return command.ExecuteScalar() as Guid?;
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

    private sealed class SuppressingOutcomeStore : INotificationIntentStore
    {
        private readonly SqlServerNotificationIntentStore _inner;

        public SuppressingOutcomeStore(TradingEngineDbContext context)
        {
            _inner = new SqlServerNotificationIntentStore(context);
        }

        public Task<IReadOnlyList<PendingNotification>> ListDispatchableAsync(
            int maxCount,
            Instant now,
            CancellationToken cancellationToken)
        {
            return _inner.ListDispatchableAsync(maxCount, now, cancellationToken);
        }

        public Task<NotificationClaim?> TryClaimAsync(
            Guid intentId,
            Guid claimToken,
            Instant claimedUntil,
            Instant now,
            CancellationToken cancellationToken)
        {
            return _inner.TryClaimAsync(
                intentId,
                claimToken,
                claimedUntil,
                now,
                cancellationToken);
        }

        public Task<BeginSubmissionResult> TryBeginSubmissionAsync(
            Guid intentId,
            Guid claimToken,
            string providerOperationId,
            int maxAttempts,
            Instant now,
            CancellationToken cancellationToken)
        {
            return _inner.TryBeginSubmissionAsync(
                intentId,
                claimToken,
                providerOperationId,
                maxAttempts,
                now,
                cancellationToken);
        }

        public Task<bool> RecordOutcomeAsync(
            DispatchOutcome outcome,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
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
