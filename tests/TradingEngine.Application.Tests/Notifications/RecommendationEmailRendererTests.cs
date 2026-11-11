using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.Tests.Notifications;

[TestFixture]
public sealed class RecommendationEmailRendererTests
{
    private static readonly Guid InstrumentId =
        Guid.Parse("3f8a2c1d-9e4b-4a5c-b7d6-2e1f0a9b8c7d");
    private static readonly Guid RevisionId =
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Instant Slot = Instant.FromUtc(2026, 10, 30, 13, 44, 0);

    [Test]
    public void Render_WhenIntentHasBuyZoneMatch_IncludesRequiredFields()
    {
        PendingNotification intent = new(
            Guid.CreateVersion7(),
            InstrumentId,
            "DEMO",
            "XNAS",
            Slot,
            Slot.Minus(Duration.FromMinutes(1)),
            RevisionId,
            PriceBasis.Bid,
            "USD",
            100.25m,
            101.50m,
            [
                new TriggeredCondition(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    ChartConditionType.BuyZone,
                    ChartAnalysisIdentifier.From("notify").Value,
                    95m,
                    100m,
                    105m,
                    120m,
                    100.25m)
            ],
            NotificationStatus.Pending,
            0,
            null);

        RecommendationEmail email = new RecommendationEmailRenderer().Render(intent);

        Assert.Multiple(() =>
        {
            Assert.That(email.Subject, Does.Contain("DEMO"));
            Assert.That(email.Subject, Does.Contain("buy-zone entry"));
            Assert.That(email.PlainTextBody, Does.Contain("DEMO (XNAS)"));
            Assert.That(email.PlainTextBody, Does.Contain("2026-10-30T13:44:00Z"));
            Assert.That(email.PlainTextBody, Does.Contain("100.25 USD (Bid basis)"));
            Assert.That(email.PlainTextBody, Does.Contain("95 - 105"));
            Assert.That(email.PlainTextBody, Does.Contain("Reason:"));
            Assert.That(
                email.PlainTextBody,
                Does.Contain(RevisionId.ToString()));
            Assert.That(
                email.PlainTextBody,
                Does.Contain("no order was placed"));
        });
    }
}
