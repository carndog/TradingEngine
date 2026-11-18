using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Signals;
using TradingEngine.Application.WeeklyReports;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class WeeklyReportPersistenceTests
{
    private static readonly Instant Cutoff = Instant.FromUtc(2026, 1, 11, 18, 0);
    private static readonly WeeklyReportPeriod Period = new(Cutoff.Minus(Duration.FromDays(7)), Cutoff, Cutoff.Plus(Duration.FromDays(7)));
    private SignalTestDatabase _database = null!;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        _database = new SignalTestDatabase();
        await _database.StartAsync();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await _database.DisposeAsync();
    }

    [Test]
    public async Task TryClaimGenerationAsync_ConcurrentHosts_CreateOneLogicalPeriod()
    {
        string key = await ScheduleAsync();
        WeeklyReportGenerationRequest first = Request(key);
        WeeklyReportGenerationRequest second = first with { ClaimToken = Guid.NewGuid() };

        WeeklyReportClaim?[] claims = await Task.WhenAll(ClaimAsync(first), ClaimAsync(second));
        await using TradingEngineDbContext context = _database.CreateContext();

        Assert.That(claims.Count(claim => claim is not null), Is.EqualTo(1));
        Assert.That(await context.WeeklyReports.CountAsync(row => row.SubscriptionKey == key), Is.EqualTo(1));
    }

    [Test]
    public async Task CompleteGenerationAsync_FailedCompositionAndRestart_ReclaimsOriginalCutoffAndRecipients()
    {
        string key = await ScheduleAsync();
        WeeklyReportGenerationRequest request = Request(key);
        WeeklyReportClaim first = (await ClaimAsync(request))!;
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerWeeklyReportStore store = new(context);
        Assert.That(async () => await store.CompleteGenerationAsync(first,
            _ => throw new InvalidOperationException("synthetic composition failure"), Cutoff, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());

        WeeklyReportClaim recovered = (await ClaimAsync(request with
        {
            Now = request.ClaimedUntil, ClaimedUntil = request.ClaimedUntil.Plus(Duration.FromMinutes(5)),
            ClaimToken = Guid.NewGuid(), Recipients = ["changed@synthetic.example"]
        }))!;

        Assert.That(recovered.ReportId, Is.EqualTo(first.ReportId));
        Assert.That(recovered.Period, Is.EqualTo(first.Period));
        Assert.That(recovered.Recipients, Is.EqualTo(first.Recipients));
        Assert.That(await context.WeeklyReportItems.CountAsync(row => row.ReportId == first.ReportId), Is.Zero);
        Assert.That(await store.CompleteGenerationAsync(first, _ => Task.FromResult(Empty(first)), request.ClaimedUntil, CancellationToken.None), Is.False);
    }

    [Test]
    public async Task CompleteGenerationAsync_SnapshotConstraintFailure_RollsBackItemsAndDeliveryReadiness()
    {
        string key = await ScheduleAsync();
        WeeklyReportClaim claim = (await ClaimAsync(Request(key)))!;
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerWeeklyReportStore store = new(context);
        WeeklyReportSnapshot invalid = Snapshot(Guid.NewGuid(), claim.Recipients);

        Assert.That(async () => await store.CompleteGenerationAsync(claim, _ => Task.FromResult(invalid), Cutoff, CancellationToken.None),
            Throws.TypeOf<DbUpdateException>());

        WeeklyReportRow row = await context.WeeklyReports.AsNoTracking().SingleAsync(item => item.Id == claim.ReportId);
        Assert.That(row.GenerationStatus, Is.EqualTo("Generating"));
        Assert.That(row.DeliveryStatus, Is.EqualTo("Skipped"));
        Assert.That(await context.WeeklyReportItems.CountAsync(item => item.ReportId == claim.ReportId), Is.Zero);
        Assert.That(await store.ListDispatchableAsync(key, 10, Cutoff, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task CompleteGenerationAsync_EmptyPeriod_AdvancesDurableScheduleWithoutEmail()
    {
        string key = await ScheduleAsync();
        WeeklyReportClaim claim = (await ClaimAsync(Request(key)))!;
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerWeeklyReportStore store = new(context);

        Assert.That(await store.CompleteGenerationAsync(claim, _ => Task.FromResult(Empty(claim)), Cutoff, CancellationToken.None), Is.True);
        Assert.That(await store.ListDispatchableAsync(key, 10, Cutoff, CancellationToken.None), Is.Empty);
        Assert.That((await store.ReadAsync(key, claim.ReportId, 1, 25, CancellationToken.None))!.GenerationStatus, Is.EqualTo("Empty"));
        Assert.That((await context.WeeklyReportSchedules.AsNoTracking().SingleAsync(row => row.SubscriptionKey == key)).NextCutoff, Is.EqualTo(Period.NextCutoff));
        Assert.That(await store.TryClaimGenerationAsync(Request(key), CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task CompleteGenerationAsync_CompletedSnapshot_IsImmutableAndIsolatedBySubscription()
    {
        (string key, WeeklyReportClaim claim) = await ReadyAsync();
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerWeeklyReportStore store = new(context);
        bool invoked = false;

        bool overwritten = await store.CompleteGenerationAsync(claim, _ =>
        {
            invoked = true;
            return Task.FromResult(Empty(claim));
        }, Cutoff, CancellationToken.None);
        IReadOnlyList<EmailDeliveryWork> deliveries = await store.ListDispatchableAsync(key, 10, Cutoff, CancellationToken.None);
        WeeklyReportPage page = (await store.ReadAsync(key, claim.ReportId, 1, 1, CancellationToken.None))!;

        Assert.That(overwritten, Is.False);
        Assert.That(invoked, Is.False);
        Assert.That(page.TotalCount, Is.EqualTo(1));
        Assert.That(page.Items.Single().TriggerPrice, Is.EqualTo(100m));
        Assert.That((await store.ListAsync(key, 25, CancellationToken.None)).Single().ReportId, Is.EqualTo(claim.ReportId));
        Assert.That(deliveries.Single().Render().RecipientAddresses, Is.EqualTo(claim.Recipients));
        Assert.That(await store.ReadAsync("different-deployment", claim.ReportId, 1, 1, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task TryBeginSubmissionAsync_CrashBeforeOutcome_PreservesOperationForReconciliation()
    {
        (string key, WeeklyReportClaim report) = await ReadyAsync();
        Guid claim = Guid.NewGuid();
        Guid nextClaim = Guid.NewGuid();
        string operation = report.ReportId.ToString("D");
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerWeeklyReportStore store = new(context);
        Assert.That(await store.TryClaimAsync(report.ReportId, claim, Cutoff.Plus(Duration.FromMinutes(1)), Cutoff, CancellationToken.None), Is.Not.Null);

        Assert.That(await store.TryBeginSubmissionAsync(report.ReportId, claim, operation, 3, Cutoff, CancellationToken.None), Is.EqualTo(BeginSubmissionResult.Began));
        NotificationClaim recovered = (await store.TryClaimAsync(report.ReportId, nextClaim, Cutoff.Plus(Duration.FromMinutes(3)), Cutoff.Plus(Duration.FromMinutes(1)), CancellationToken.None))!;

        Assert.That(recovered.ProviderOperationId, Is.EqualTo(operation));
        Assert.That(recovered.Status, Is.EqualTo(NotificationStatus.Pending));
        Assert.That(await context.WeeklyReportAttempts.CountAsync(row => row.ReportId == report.ReportId && row.Outcome == "SubmissionStarted"), Is.EqualTo(1));
        bool stale = await store.RecordOutcomeAsync(new EmailDeliveryOutcome(report.ReportId, claim, NotificationStatus.Sent,
            NotificationAttemptOutcome.ResolvedSucceeded, Cutoff, Cutoff, operation, "Succeeded", null), CancellationToken.None);
        Assert.That(stale, Is.False);
        Assert.That(await store.TryBeginSubmissionAsync(report.ReportId, nextClaim, operation, 3, Cutoff.Plus(Duration.FromMinutes(1)), CancellationToken.None), Is.EqualTo(BeginSubmissionResult.ClaimLost));
        Assert.That(await context.NotificationAttempts.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ConfigureScheduleAsync_DisableRestartAndReenable_SuppressesDeliveryAndSchedulesNextOccurrence()
    {
        (string key, WeeklyReportClaim report) = await ReadyAsync();
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerWeeklyReportStore store = new(context);
        Instant future = Cutoff.Plus(Duration.FromDays(21));

        Assert.That(await store.ConfigureScheduleAsync(key, true, "synthetic-weekly", future, Cutoff, CancellationToken.None), Is.EqualTo(Period.NextCutoff));
        await store.ConfigureScheduleAsync(key, false, "synthetic-weekly", future, Cutoff, CancellationToken.None);
        Assert.That(await store.ListDispatchableAsync(key, 10, Cutoff, CancellationToken.None), Is.Empty);
        Assert.That(await store.ConfigureScheduleAsync(key, true, "synthetic-weekly", future, Cutoff, CancellationToken.None), Is.EqualTo(future));
        Assert.That((await store.ListDispatchableAsync(key, 10, Cutoff, CancellationToken.None)).Single().Id, Is.EqualTo(report.ReportId));
    }

    private async Task<(string Key, WeeklyReportClaim Claim)> ReadyAsync()
    {
        string key = await ScheduleAsync();
        NotificationIntentMatchRow signal = await _database.SeedAsync();
        WeeklyReportClaim claim = (await ClaimAsync(Request(key)))!;
        await using TradingEngineDbContext context = _database.CreateContext();
        await new SqlServerWeeklyReportStore(context).CompleteGenerationAsync(claim,
            _ => Task.FromResult(Snapshot(signal.Id, claim.Recipients)), Cutoff, CancellationToken.None);
        return (key, claim);
    }

    private async Task<string> ScheduleAsync()
    {
        string key = $"synthetic:{Guid.NewGuid():N}";
        await using TradingEngineDbContext context = _database.CreateContext();
        await new SqlServerWeeklyReportStore(context).ConfigureScheduleAsync(key, true, "synthetic-weekly", Cutoff, Cutoff.Minus(Duration.FromDays(1)), CancellationToken.None);
        return key;
    }

    private async Task<WeeklyReportClaim?> ClaimAsync(WeeklyReportGenerationRequest request)
    {
        await using TradingEngineDbContext context = _database.CreateContext();
        return await new SqlServerWeeklyReportStore(context).TryClaimGenerationAsync(request, CancellationToken.None);
    }

    private static WeeklyReportGenerationRequest Request(string key)
    {
        return new WeeklyReportGenerationRequest(key, Cutoff, Period, 0, ["snapshot@synthetic.example"], Guid.NewGuid(), Cutoff.Plus(Duration.FromMinutes(5)), Cutoff);
    }

    private static WeeklyReportSnapshot Empty(WeeklyReportClaim claim)
    {
        return new WeeklyReportSnapshot([], new RecommendationEmail("empty", "empty", RecipientAddresses: claim.Recipients));
    }

    private static WeeklyReportSnapshot Snapshot(Guid signalId, IReadOnlyList<string> recipients)
    {
        SignalStatus signal = new(signalId, Guid.NewGuid(), Guid.NewGuid(), "SYNTH", "XTEST", Guid.NewGuid(),
            "synthetic-zone", "BuyZone", "publish-signal", SignalTestDatabase.TriggeredAt.ToDateTimeOffset(), 100m, "Bid", "USD", 95m, 105m,
            null, null, null, 110m, 10m, "Above", null, "current", null, "Sent", false, false, true, false, null);
        return new WeeklyReportSnapshot([signal], new RecommendationEmail("synthetic weekly report", "immutable report", "<p>immutable report</p>", recipients));
    }
}
