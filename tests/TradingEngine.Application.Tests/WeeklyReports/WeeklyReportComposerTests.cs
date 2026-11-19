using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Signals;
using TradingEngine.Application.Tests.Signals;
using TradingEngine.Application.WeeklyReports;

namespace TradingEngine.Application.Tests.WeeklyReports;

[TestFixture]
public sealed class WeeklyReportComposerTests
{
    private static readonly Instant Cutoff = Instant.FromUtc(2026, 1, 18, 18, 0);

    [Test]
    public async Task ComposeAsync_NoNewSignals_KeepsAllTrackedAndOnlyRecentArchiveActivity()
    {
        WeeklyReportOptions options = new();
        WeeklyReportPeriod period = new WeeklyReportSchedule(options).PeriodEndingAt(Cutoff);
        WeeklySignalReader reader = new();
        for (int index = 0; index < 102; index++)
        {
            SignalEvidence item = ReadSignalsHandlerTests.Evidence();
            reader.Items.Add(item with { Baseline = item.Baseline with { TriggeredAt = period.StartsAt.Minus(Duration.FromDays(10)) } });
        }

        SignalEvidence recent = ReadSignalsHandlerTests.Evidence() with { Archived = true, TrackingChangedAt = period.StartsAt.PlusTicks(1).ToDateTimeOffset() };
        reader.Items.Add(recent);
        reader.Items.Add(recent with { SignalId = Guid.NewGuid(), TrackingChangedAt = period.StartsAt.ToDateTimeOffset() });
        WeeklyReportSnapshot snapshot = await Composer(reader, options).ComposeAsync(period, Guid.NewGuid(), ["recipient@example.test"], 2, CancellationToken.None);

        Assert.That(snapshot.Items, Has.Count.EqualTo(103));
        Assert.That(snapshot.Items.Select(item => item.SignalId).Distinct().Count(), Is.EqualTo(103));
        Assert.That(snapshot.Items.Count(item => item.Archived), Is.EqualTo(1));
        Assert.That(reader.Queries, Has.Count.EqualTo(3));
        Assert.That(reader.Queries.All(query => query.Cutoff == Cutoff && query.PageSize == 100), Is.True);
        Assert.That(snapshot.Email.PlainTextBody, Does.Contain("new this period: 0"));
        Assert.That(snapshot.Email.PlainTextBody, Does.Contain("Older scheduled periods skipped: 2"));
    }

    [Test]
    public void ComposeAsync_SnapshotLimitExceeded_FailsWithoutPartialMessage()
    {
        WeeklyReportOptions options = new() { MaxSnapshotRows = 1 };
        WeeklySignalReader reader = new();
        reader.Items.AddRange([ReadSignalsHandlerTests.Evidence(), ReadSignalsHandlerTests.Evidence()]);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Composer(reader, options).ComposeAsync(
            new WeeklyReportSchedule(options).PeriodEndingAt(Cutoff), null, [], 0, CancellationToken.None));
    }

    [Test]
    public void ComposeAsync_Cancelled_DoesNotRead()
    {
        WeeklySignalReader reader = new();
        WeeklyReportOptions options = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await Composer(reader, options).ComposeAsync(
            new WeeklyReportSchedule(options).PeriodEndingAt(Cutoff), null, [], 0, cancellation.Token));
        Assert.That(reader.Queries, Is.Empty);
    }

    private static WeeklyReportComposer Composer(WeeklySignalReader reader, WeeklyReportOptions options)
    {
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession> { ["XTEST"] = new(null, true, []) });
        ReadSignalsHandler handler = new(reader, new SignalFreshnessClassifier(sessions), new FakeClock(Cutoff));
        return new WeeklyReportComposer(handler, new WeeklyReportEmailRenderer(options), options);
    }
}
