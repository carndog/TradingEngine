using System.Text;
using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Signals;
using TradingEngine.Application.WeeklyReports;

namespace TradingEngine.Application.Tests.WeeklyReports;

[TestFixture]
public sealed class WeeklyReportEmailRendererTests
{
    private static readonly Instant Cutoff = Instant.FromUtc(2026, 1, 18, 18, 0);

    [TestCase(10, "+10.00%")]
    [TestCase(-5, "-5.00%")]
    [TestCase(0, "0.00%")]
    public void Render_PercentageAndUntrustedContent_PreservesMeaningAndEncodesHtml(decimal change, string expected)
    {
        WeeklyReportOptions options = new();
        SignalStatus item = Item() with { PercentageChange = change, Symbol = "<script>&\"synthetic\"", QuoteFreshness = "stale_last_known" };
        RecommendationEmail email = new WeeklyReportEmailRenderer(options).Render(
            new WeeklyReportSchedule(options).PeriodEndingAt(Cutoff), Guid.NewGuid(), [item], ["recipient@example.test"], 0);

        Assert.That(email.PlainTextBody, Does.Contain(expected));
        Assert.That(email.PlainTextBody, Does.Contain("stale_last_known"));
        Assert.That(email.HtmlBody, Does.Not.Contain("<script>"));
        Assert.That(email.HtmlBody, Does.Contain("&lt;script&gt;&amp;&quot;synthetic&quot;"));
        Assert.That(email.PlainTextBody, Does.Contain("unadjusted observed price movement"));
    }

    [Test]
    public void Render_RowLimit_DisclosesTotalAndLinksToSavedEvidence()
    {
        WeeklyReportOptions options = new() { MaxEmailRows = 1, BaseUrl = "https://example.test" };
        Guid id = Guid.NewGuid();
        RecommendationEmail email = new WeeklyReportEmailRenderer(options).Render(
            new WeeklyReportSchedule(options).PeriodEndingAt(Cutoff), id, [Item(), Item()], [], 0);
        Assert.That(email.PlainTextBody, Does.Contain("Showing 1 of 2 signal rows"));
        Assert.That(email.PlainTextBody, Does.Contain($"https://example.test/api/weekly-reports/{id}?page=1&pageSize=100"));
    }

    [Test]
    public void Render_ByteLimit_BoundsUtf8AndPreservesContinuation()
    {
        WeeklyReportOptions options = new() { MaxEmailBytes = 4096 };
        SignalStatus item = Item() with { Symbol = new string('é', 4000) };
        RecommendationEmail email = new WeeklyReportEmailRenderer(options).Render(
            new WeeklyReportSchedule(options).PeriodEndingAt(Cutoff), null, [item], [], 0);
        int bytes = Encoding.UTF8.GetByteCount(email.Subject + email.PlainTextBody + email.HtmlBody);
        Assert.That(bytes, Is.LessThanOrEqualTo(options.MaxEmailBytes));
        Assert.That(email.PlainTextBody, Does.Contain("Showing 0 of 1 signal rows"));
        Assert.That(email.PlainTextBody, Does.Contain("/api/weekly-reports/preview?cutoff="));
    }

    [Test]
    public void Render_UnavailableChange_ExplainsReasonInsteadOfZero()
    {
        WeeklyReportOptions options = new();
        SignalStatus item = Item() with { PercentageChange = null, ComparisonUnavailableReason = "NoLaterObservation" };
        RecommendationEmail email = new WeeklyReportEmailRenderer(options).Render(
            new WeeklyReportSchedule(options).PeriodEndingAt(Cutoff), null, [item], [], 0);
        Assert.That(email.PlainTextBody, Does.Contain("unavailable (NoLaterObservation)"));
        Assert.That(email.PlainTextBody, Does.Not.Contain("0.00%"));
    }

    private static SignalStatus Item()
    {
        return new SignalStatus(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SYNTH", "XTEST", Guid.NewGuid(),
            "synthetic-zone", "BuyZone", "publish-signal", Cutoff.Minus(Duration.FromDays(1)).ToDateTimeOffset(),
            100m, "Bid", "USD", 95m, 105m, Guid.NewGuid(), Cutoff.ToDateTimeOffset(), Cutoff.ToDateTimeOffset(),
            110m, 10m, "Above", null, "current", 0, "Sent", false, false, true, false, null);
    }
}
