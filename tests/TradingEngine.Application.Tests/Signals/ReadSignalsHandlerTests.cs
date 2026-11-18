using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Signals;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.SignalTracking;

namespace TradingEngine.Application.Tests.Signals;

[TestFixture]
public sealed class ReadSignalsHandlerTests
{
    private static readonly Instant Cutoff = Instant.FromUtc(2026, 1, 12, 12, 0);

    [Test]
    public async Task HandleAsync_OriginalEvidence_ReturnsIndependentMovementRangeAndContext()
    {
        SignalEvidence signal = Evidence() with { MonitoringPaused = true, InstrumentRemoved = true, SourceRevisionActive = false, EmailStatus = "Failed" };
        SignalEvidenceStub reader = new() { Items = [signal] };

        Result<SignalPage> result = await Handler(reader).HandleAsync(new SignalQuery(Cutoff), CancellationToken.None);
        SignalStatus item = result.Value.Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.SignalId, Is.EqualTo(signal.SignalId));
            Assert.That(item.PercentageChange, Is.EqualTo(10m));
            Assert.That(item.RangePosition, Is.EqualTo("Above"));
            Assert.That(item.MonitoringPaused, Is.True);
            Assert.That(item.InstrumentRemoved, Is.True);
            Assert.That(item.SourceRevisionActive, Is.False);
            Assert.That(item.EmailStatus, Is.EqualTo("Failed"));
        });
    }

    [Test]
    public async Task HandleAsync_NoLaterPrice_ReturnsUnavailableWithoutZero()
    {
        SignalEvidenceStub reader = new() { Items = [Evidence() with { Quote = null }] };

        SignalStatus item = (await Handler(reader).HandleAsync(new SignalQuery(Cutoff), CancellationToken.None)).Value.Items.Single();

        Assert.That(item.PercentageChange, Is.Null);
        Assert.That(item.ComparisonUnavailableReason, Is.EqualTo("NoLaterObservation"));
        Assert.That(item.QuoteFreshness, Is.EqualTo("missing"));
    }

    [Test]
    public async Task HandleAsync_InvalidStoredQuote_IsUnavailable()
    {
        SignalEvidence source = Evidence();
        SignalEvidenceStub reader = new() { Items = [source with { Quote = source.Quote! with { Bid = 0m } }] };

        SignalStatus item = (await Handler(reader).HandleAsync(new SignalQuery(Cutoff), CancellationToken.None)).Value.Items.Single();

        Assert.That(item.PercentageChange, Is.Null);
        Assert.That(item.ComparisonUnavailableReason, Is.EqualTo("InvalidLatestQuote"));
    }

    [TestCase(0, 25)]
    [TestCase(1, 0)]
    [TestCase(1, 101)]
    [TestCase(int.MaxValue, 100)]
    public async Task HandleAsync_InvalidPagination_DoesNotRead(int page, int size)
    {
        SignalEvidenceStub reader = new();

        Result<SignalPage> result = await Handler(reader).HandleAsync(new SignalQuery(Cutoff, Page: page, PageSize: size), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(ReadSignalsHandler.InvalidQuery));
        Assert.That(reader.Calls, Is.Zero);
    }

    [Test]
    public async Task HandleAsync_FutureCutoff_DoesNotRead()
    {
        SignalEvidenceStub reader = new();
        Result<SignalPage> result = await Handler(reader).HandleAsync(new SignalQuery(Cutoff.PlusTicks(1)), CancellationToken.None);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(reader.Calls, Is.Zero);
    }

    [Test]
    public async Task HandleAsync_ExtraRow_EmitsBoundedPageWithContinuation()
    {
        SignalEvidenceStub reader = new() { Items = [Evidence(), Evidence()] };
        SignalPage page = (await Handler(reader).HandleAsync(new SignalQuery(Cutoff, PageSize: 1), CancellationToken.None)).Value;
        Assert.That(page.Items, Has.Count.EqualTo(1));
        Assert.That(page.HasMore, Is.True);
        Assert.That(page.Cutoff, Is.EqualTo(Cutoff.ToDateTimeOffset()));
    }

    internal static SignalEvidence Evidence()
    {
        return new SignalEvidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SYNTH", "XTEST", Guid.NewGuid(),
            "synthetic-zone", "BuyZone", "publish-signal",
            new SignalPriceBaseline(Cutoff.Minus(Duration.FromDays(2)), 100m, PriceBasis.Bid, "USD", 95m, 105m),
            "Sent", false, false, true, 900,
            new SignalQuote(Guid.NewGuid(), Cutoff.Minus(Duration.FromMinutes(15)), Cutoff.Minus(Duration.FromMinutes(14)),
                null, 110m, 111m, "USD"));
    }

    private static ReadSignalsHandler Handler(SignalEvidenceStub reader)
    {
        WeeklyScheduleMarketSessionPolicy sessions = new(new Dictionary<string, ExchangeSession>
        {
            ["XTEST"] = new(null, true, [])
        });
        return new ReadSignalsHandler(reader, new SignalFreshnessClassifier(sessions), new FakeClock(Cutoff));
    }
}
