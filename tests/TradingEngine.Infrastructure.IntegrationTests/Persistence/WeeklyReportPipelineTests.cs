using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Signals;
using TradingEngine.Application.WeeklyReports;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class WeeklyReportPipelineTests : IEmailSender
{
    private static readonly Instant Cutoff = Instant.FromUtc(2026, 1, 11, 18, 0);
    private SignalTestDatabase _database = null!;
    private readonly List<RecommendationEmail> _sent = [];
    private int _probes;

    [SetUp]
    public async Task SetUp()
    {
        _database = new SignalTestDatabase();
        await _database.StartAsync();
        _sent.Clear();
        _probes = 0;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Test]
    public async Task HandleAsync_FirstEnableDuplicateTicksAndRestart_UsesFrozenEvidenceAndReconcilesOnce()
    {
        WeeklyReportOptions options = Options();
        NotificationIntentMatchRow signal = await _database.SeedAsync();
        await _database.QuoteAsync(signal.Intent.WatchedInstrumentId, Cutoff.Minus(Duration.FromMinutes(15)));
        await _database.QuoteAsync(signal.Intent.WatchedInstrumentId, Cutoff.Plus(Duration.FromMinutes(15)), 130m);

        await RunAsync(options, Cutoff.Minus(Duration.FromMinutes(1)));
        Assert.That(_sent, Is.Empty);
        WeeklyReportRunResult generated = await RunAsync(options, Cutoff.Plus(Duration.FromHours(1)));
        await _database.QuoteAsync(signal.Intent.WatchedInstrumentId, Cutoff.Plus(Duration.FromHours(2)), 150m);
        await RunAsync(options, Cutoff.Plus(Duration.FromHours(3)));
        await RunAsync(options, Cutoff.Plus(Duration.FromHours(3)));
        await using TradingEngineDbContext context = _database.CreateContext();
        WeeklyReportRow report = await context.WeeklyReports.AsNoTracking().SingleAsync();
        WeeklyReportPage page = (await new SqlServerWeeklyReportStore(context).ReadAsync(options.SubscriptionKey, report.Id, 1, 25, CancellationToken.None))!;

        Assert.That(generated.Generated, Is.True);
        Assert.That(_sent, Has.Count.EqualTo(1));
        Assert.That(_probes, Is.EqualTo(1));
        Assert.That(page.Items.Single().PercentageChange, Is.EqualTo(10m));
        Assert.That(page.DeliveryStatus, Is.EqualTo("Sent"));
        Assert.That(page.PlainTextBody, Is.EqualTo(_sent.Single().PlainTextBody));
        Assert.That(await context.WeeklyReportAttempts.CountAsync(), Is.EqualTo(3));
        Assert.That(await context.NotificationAttempts.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task HandleAsync_Downtime_ProducesOnlyLatestMissedWeekAndRecordsSkippedPeriods()
    {
        WeeklyReportOptions options = Options();
        await _database.SeedAsync();
        await RunAsync(options, Cutoff.Minus(Duration.FromDays(1)));
        await RunAsync(options, Cutoff.Plus(Duration.FromDays(22)));
        await using TradingEngineDbContext context = _database.CreateContext();
        WeeklyReportRow report = await context.WeeklyReports.AsNoTracking().SingleAsync();
        Assert.That(report.Cutoff, Is.EqualTo(Cutoff.Plus(Duration.FromDays(21))));
        Assert.That(report.SkippedPeriods, Is.EqualTo(3));
        Assert.That(report.FirstSkippedCutoff, Is.EqualTo(Cutoff));
        Assert.That(_sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task HandleAsync_CompositionFailure_RecordsErrorAndRecoversOriginalClaimAfterLease()
    {
        WeeklyReportOptions options = Options();
        options.MaxSnapshotRows = 1;
        await _database.SeedAsync();
        await _database.SeedAsync();
        await RunAsync(options, Cutoff.Minus(Duration.FromDays(1)));
        WeeklyReportRunResult failed = await RunAsync(options, Cutoff);
        Assert.That(failed.GenerationFailed, Is.True);
        Assert.That(_sent, Is.Empty);
        await using TradingEngineDbContext context = _database.CreateContext();
        WeeklyReportRow pending = await context.WeeklyReports.AsNoTracking().SingleAsync();
        Assert.That(pending.GenerationError, Is.EqualTo("generation_failed"));
        Assert.That(await context.WeeklyReportItems.CountAsync(), Is.Zero);

        options.MaxSnapshotRows = 10;
        options.RecipientAddresses = ["changed@example.test"];
        await RunAsync(options, Cutoff.Plus(Duration.FromMinutes(11)));
        WeeklyReportRow recovered = await context.WeeklyReports.AsNoTracking().SingleAsync();
        Assert.That(recovered.Id, Is.EqualTo(pending.Id));
        Assert.That(recovered.GenerationError, Is.Null);
        Assert.That(_sent.Single().RecipientAddresses, Is.EqualTo(new[] { "recipient@example.test" }));
    }

    [Test]
    public async Task HandleAsync_EmptyPeriod_AdvancesWithoutSendAndDisablePersistsAcrossRestart()
    {
        WeeklyReportOptions options = Options();
        await RunAsync(options, Cutoff.Minus(Duration.FromDays(1)));
        await RunAsync(options, Cutoff);
        options.Enabled = false;
        await RunAsync(options, Cutoff.Plus(Duration.FromDays(1)));
        options.Enabled = true;
        await RunAsync(options, Cutoff.Plus(Duration.FromDays(22)));
        await using TradingEngineDbContext context = _database.CreateContext();
        Assert.That((await context.WeeklyReports.SingleAsync()).GenerationStatus, Is.EqualTo("Empty"));
        Assert.That((await context.WeeklyReportSchedules.SingleAsync()).NextCutoff, Is.EqualTo(Cutoff.Plus(Duration.FromDays(28))));
        Assert.That(_sent, Is.Empty);
    }

    public Task<EmailSendResult> SendAsync(RecommendationEmail email, Guid operationId, CancellationToken cancellationToken)
    {
        _sent.Add(email);
        return Task.FromResult(new EmailSendResult(EmailSendOutcome.Accepted, operationId.ToString("D"), null));
    }

    public Task<EmailOperationProbe> GetOperationStatusAsync(string operationId, CancellationToken cancellationToken)
    {
        _probes++;
        return Task.FromResult(new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
    }

    private async Task<WeeklyReportRunResult> RunAsync(WeeklyReportOptions options, Instant now)
    {
        await using TradingEngineDbContext context = _database.CreateContext();
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession> { ["XTEST"] = new(null, true, []) });
        BatchTestClock clock = new() { Now = now };
        ReadSignalsHandler reader = new(new SqlServerSignalEvidenceReader(context), new SignalFreshnessClassifier(sessions), clock);
        WeeklyReportComposer composer = new(reader, new WeeklyReportEmailRenderer(options), options);
        RunWeeklyReportsHandler handler = new(new SqlServerWeeklyReportStore(context), new WeeklyReportSchedule(options), composer, options, clock, this);
        return await handler.HandleAsync(CancellationToken.None);
    }

    private static WeeklyReportOptions Options()
    {
        return new WeeklyReportOptions
        {
            Enabled = true, DeploymentId = "synthetic", BaseUrl = "https://example.test", RecipientAddresses = ["recipient@example.test"]
        };
    }
}
