using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Tests.PriceCollection;

[TestFixture]
public sealed class SamplingPolicyTests
{
    private static readonly MonitoredInstrument Instrument = new(
        Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d"),
        "XNAS",
        "DEMO",
        "USD",
        60);

    [Test]
    public void EffectiveIntervalSeconds_WhenPerInstrument_ReturnsStoredInterval()
    {
        ISamplingPolicy policy = new PerInstrumentSamplingPolicy();

        int interval = policy.EffectiveIntervalSeconds(Instrument, 50);

        Assert.That(interval, Is.EqualTo(60));
    }

    [Test]
    public void EffectiveIntervalSeconds_WhenBelowProviderMinimum_ClampsUp()
    {
        ISamplingPolicy policy = new ProviderBudgetClampedSamplingPolicy(120, 60);
        MonitoredInstrument fast = Instrument with { SamplingIntervalSeconds = 1 };

        int interval = policy.EffectiveIntervalSeconds(fast, 10);

        Assert.That(interval, Is.EqualTo(5));
    }

    [Test]
    public void EffectiveIntervalSeconds_WhenAboveProviderMinimum_ReturnsStoredInterval()
    {
        ISamplingPolicy policy = new ProviderBudgetClampedSamplingPolicy(120, 60);

        int interval = policy.EffectiveIntervalSeconds(Instrument, 10);

        Assert.That(interval, Is.EqualTo(60));
    }

    [Test]
    public void EffectiveIntervalSeconds_WhenCountIsZero_TreatsAsOne()
    {
        ISamplingPolicy policy = new ProviderBudgetClampedSamplingPolicy(120, 60);
        MonitoredInstrument fast = Instrument with { SamplingIntervalSeconds = 1 };

        int interval = policy.EffectiveIntervalSeconds(fast, 0);

        Assert.That(interval, Is.EqualTo(1));
    }

    [TestCase(1000, 1)]
    [TestCase(1001, 1)]
    [TestCase(2001, 2)]
    [TestCase(10000, 5)]
    public void EffectiveIntervalSeconds_BatchProvider_BudgetsRequestsInsteadOfQuotes(int count, int expected)
    {
        ISamplingPolicy policy = new ProviderBudgetClampedSamplingPolicy(120, 60);
        MonitoredInstrument fast = Instrument with { SamplingIntervalSeconds = 1 };

        int interval = policy.EffectiveIntervalSeconds(fast, count, 1000);

        Assert.That(interval, Is.EqualTo(expected));
    }

    [Test]
    public void EffectiveIntervalSeconds_TenThousandStocksAtFifteenMinutes_PreservesRequestedInterval()
    {
        ISamplingPolicy policy = new ProviderBudgetClampedSamplingPolicy(120, 60);
        MonitoredInstrument instrument = Instrument with { SamplingIntervalSeconds = 900 };

        int interval = policy.EffectiveIntervalSeconds(instrument, 10000, 1000);

        Assert.That(interval, Is.EqualTo(900));
    }

    [Test]
    public void Resolve_WhenNameIsNullOrEmpty_ReturnsPerInstrument()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                SamplingPolicyResolver.Resolve(null, 120, 60),
                Is.TypeOf<PerInstrumentSamplingPolicy>());
            Assert.That(
                SamplingPolicyResolver.Resolve("   ", 120, 60),
                Is.TypeOf<PerInstrumentSamplingPolicy>());
        });
    }

    [Test]
    public void Resolve_WhenNamedPoliciesRequested_ReturnsMatchingPolicy()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                SamplingPolicyResolver.Resolve("per-instrument", 120, 60),
                Is.TypeOf<PerInstrumentSamplingPolicy>());
            Assert.That(
                SamplingPolicyResolver.Resolve("PROVIDER-BUDGET-CLAMPED", 120, 60),
                Is.TypeOf<ProviderBudgetClampedSamplingPolicy>());
        });
    }

    [Test]
    public void Resolve_WhenNameUnknown_Throws()
    {
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => SamplingPolicyResolver.Resolve("every-tick", 120, 60));

        Assert.That(exception!.Message, Does.Contain("every-tick"));
    }

    [Test]
    public void Ctor_WhenRateLimitNotPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProviderBudgetClampedSamplingPolicy(0, 60));
    }

    [Test]
    public void Ctor_WhenWindowNotPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProviderBudgetClampedSamplingPolicy(120, 0));
    }
}
