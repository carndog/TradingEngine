using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class EToroRateLimitPoliciesTests
{
    [Test]
    public void MarketData_WhenInspected_DefinesSharedWindowQuota()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EToroRateLimitPolicies.MarketData.Name, Is.EqualTo("etoro-market-data"));
            Assert.That(EToroRateLimitPolicies.MarketData.Limit, Is.EqualTo(120));
            Assert.That(EToroRateLimitPolicies.MarketData.Window,
                Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(EToroRateLimitPolicies.MarketDataSharedEndpoints, Has.Count.EqualTo(11));
            Assert.That(EToroRateLimitPolicies.MarketDataSharedEndpoints,
                Does.Contain("GET /api/v1/market-data/search"));
            Assert.That(EToroRateLimitPolicies.MarketDataSharedEndpoints,
                Does.Contain("GET /api/v2/market-data/rates"));
        });
    }

    [Test]
    public void MinimumSamplingIntervalSeconds_ForOneInstrument_ReturnsOne()
    {
        Assert.That(EToroRateLimitPolicies.MinimumSamplingIntervalSeconds(1), Is.EqualTo(1));
    }

    [Test]
    public void MinimumSamplingIntervalSeconds_For240Instruments_Returns120()
    {
        Assert.That(EToroRateLimitPolicies.MinimumSamplingIntervalSeconds(240), Is.EqualTo(120));
    }

    [Test]
    public void MinimumSamplingIntervalSeconds_For241Instruments_Returns121()
    {
        Assert.That(EToroRateLimitPolicies.MinimumSamplingIntervalSeconds(241), Is.EqualTo(121));
    }

    [Test]
    public void MinimumSamplingIntervalSeconds_ForZeroInstruments_Throws()
    {
        Assert.That(
            () => EToroRateLimitPolicies.MinimumSamplingIntervalSeconds(0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
