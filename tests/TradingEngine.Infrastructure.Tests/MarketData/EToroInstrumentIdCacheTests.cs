using NodaTime;
using NodaTime.Testing;
using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class EToroInstrumentIdCacheTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 30, 12, 0, 0);

    private FakeClock _clock = null!;
    private EToroInstrumentIdCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _cache = new EToroInstrumentIdCache(_clock);
    }

    [Test]
    public void TryGet_WhenStoredWithinLifetime_ReturnsId()
    {
        _cache.Store("XTEST", "DEMO", "USD", 4242, TimeSpan.FromMinutes(10));

        bool found = _cache.TryGet("XTEST", "DEMO", "USD", out long instrumentId);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(instrumentId, Is.EqualTo(4242));
        });
    }

    [Test]
    public void TryGet_WhenEntryExpired_ReturnsFalse()
    {
        _cache.Store("XTEST", "DEMO", "USD", 4242, TimeSpan.FromMinutes(10));
        _clock.Advance(Duration.FromMinutes(11));

        bool found = _cache.TryGet("XTEST", "DEMO", "USD", out long instrumentId);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(instrumentId, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryGet_WithDifferentCasing_ResolvesSameEntry()
    {
        _cache.Store("xtest", "demo", "usd", 4242, TimeSpan.FromMinutes(10));

        bool found = _cache.TryGet("XTEST", "DEMO", "USD", out long instrumentId);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(instrumentId, Is.EqualTo(4242));
        });
    }
}
