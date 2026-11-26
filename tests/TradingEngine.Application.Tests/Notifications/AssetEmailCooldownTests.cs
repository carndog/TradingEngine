using NodaTime;
using TradingEngine.Application.Notifications;

namespace TradingEngine.Application.Tests.Notifications;

[TestFixture]
public sealed class AssetEmailCooldownTests
{
    private static readonly Guid Asset = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Instant Start = Instant.FromUtc(2027, 12, 31, 13, 42);
    private const string Configuration = """
        {"defaultPeriod":"P2M","overrides":[
          {"instrumentId":"aaaaaaaa-0000-4000-8000-000000000001","period":"P2D"},
          {"instrumentId":"aaaaaaaa-0000-4000-8000-000000000001","fromInclusive":10,"toExclusive":20,"period":"PT5M"}
        ]}
        """;

    [Test]
    public void IsCoolingDown_CalendarMonthEnd_ExpiresAtMatchingUtcTimeOnLastDay()
    {
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson(Configuration);
        Instant expiry = Instant.FromUtc(2028, 2, 29, 13, 42);
        Assert.That(policy.IsCoolingDown(Guid.Empty, 15m, Start, expiry - Duration.FromTicks(1)), Is.True);
        Assert.That(policy.IsCoolingDown(Guid.Empty, 15m, Start, expiry), Is.False);
    }

    [TestCase(9, 2880)]
    [TestCase(10, 5)]
    [TestCase(19, 5)]
    [TestCase(20, 2880)]
    public void IsCoolingDown_PriorAlertPrice_SelectsPriceBandOrAssetInterval(int price, int minutes)
    {
        AssetEmailCooldown policy = AssetEmailCooldown.FromJson(Configuration);
        Instant expiry = Start + Duration.FromMinutes(minutes);
        Assert.That(policy.IsCoolingDown(Asset, price, Start, expiry - Duration.FromTicks(1)), Is.True);
        Assert.That(policy.IsCoolingDown(Asset, price, Start, expiry), Is.False);
    }

    [TestCase("P0D")]
    [TestCase("P-3D")]
    [TestCase("wrong")]
    public void FromJson_InvalidInterval_RejectsSettings(string interval)
    {
        Assert.That(() => AssetEmailCooldown.FromJson($$"""{"defaultPeriod":"{{interval}}"}"""),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void FromJson_OverlappingPriceBands_RejectsSettings()
    {
        string config = $$"""
            {"defaultPeriod":"P2D","overrides":[
              {"instrumentId":"{{Asset}}","toExclusive":20,"period":"PT2H"},
              {"instrumentId":"{{Asset}}","fromInclusive":19,"period":"PT4H"}]}
            """;
        Assert.That(() => AssetEmailCooldown.FromJson(config), Throws.TypeOf<ArgumentException>());
    }
}
