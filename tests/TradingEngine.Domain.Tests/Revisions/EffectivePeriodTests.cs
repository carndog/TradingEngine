using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class EffectivePeriodTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant End = Instant.FromUtc(2026, 10, 6, 10, 0);

    [Test]
    public void Create_WithEndAfterStart_ReturnsClosedPeriod()
    {
        Result<EffectivePeriod> result = EffectivePeriod.Create(Start, End);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.EffectiveFrom, Is.EqualTo(Start));
            Assert.That(result.Value.EffectiveTo, Is.EqualTo(End));
            Assert.That(result.Value.IsOpenEnded, Is.False);
        });
    }

    [Test]
    public void Create_WithNullEnd_ReturnsOpenEndedPeriod()
    {
        Result<EffectivePeriod> result = EffectivePeriod.Create(Start, null);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.IsOpenEnded, Is.True);
    }

    [Test]
    public void Create_WithEndEqualToStart_ReturnsInvalidPeriodError()
    {
        Result<EffectivePeriod> result = EffectivePeriod.Create(Start, Start);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.InvalidPeriod));
    }

    [Test]
    public void Create_WithEndBeforeStart_ReturnsInvalidPeriodError()
    {
        Result<EffectivePeriod> result = EffectivePeriod.Create(End, Start);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.InvalidPeriod));
    }

    [Test]
    public void Contains_AtStartBoundary_ReturnsTrue()
    {
        EffectivePeriod period = EffectivePeriod.Create(Start, End).Value;

        Assert.That(period.Contains(Start), Is.True);
    }

    [Test]
    public void Contains_AtEndBoundary_ReturnsFalse()
    {
        EffectivePeriod period = EffectivePeriod.Create(Start, End).Value;

        Assert.That(period.Contains(End), Is.False);
    }

    [Test]
    public void Contains_BeforeStart_ReturnsFalse()
    {
        EffectivePeriod period = EffectivePeriod.Create(Start, End).Value;

        Assert.That(period.Contains(Start - Duration.FromSeconds(1)), Is.False);
    }

    [Test]
    public void Contains_OpenEndedFarFuture_ReturnsTrue()
    {
        EffectivePeriod period = EffectivePeriod.Create(Start, null).Value;

        Assert.That(period.Contains(Instant.FromUtc(2099, 1, 1, 0, 0)), Is.True);
    }

    [Test]
    public void HasBegun_AtStartBoundary_ReturnsTrue()
    {
        EffectivePeriod period = EffectivePeriod.Create(Start, End).Value;

        Assert.That(period.HasBegun(Start), Is.True);
    }

    [Test]
    public void HasBegun_BeforeStart_ReturnsFalse()
    {
        EffectivePeriod period = EffectivePeriod.Create(Start, End).Value;

        Assert.That(period.HasBegun(Start - Duration.FromSeconds(1)), Is.False);
    }

    [Test]
    public void Overlaps_WithAdjacentPeriod_ReturnsFalse()
    {
        EffectivePeriod first = EffectivePeriod.Create(Start, End).Value;
        EffectivePeriod second = EffectivePeriod.Create(End, null).Value;

        Assert.Multiple(() =>
        {
            Assert.That(first.Overlaps(second), Is.False);
            Assert.That(second.Overlaps(first), Is.False);
        });
    }

    [Test]
    public void Overlaps_WithIntersectingPeriod_ReturnsTrue()
    {
        EffectivePeriod first = EffectivePeriod.Create(Start, End).Value;
        EffectivePeriod second = EffectivePeriod.Create(End - Duration.FromHours(1), null).Value;

        Assert.That(first.Overlaps(second), Is.True);
    }

    [Test]
    public void Overlaps_WithContainedPeriod_ReturnsTrue()
    {
        EffectivePeriod outer = EffectivePeriod.Create(Start, null).Value;
        EffectivePeriod inner = EffectivePeriod.Create(Start + Duration.FromDays(1), End).Value;

        Assert.That(outer.Overlaps(inner), Is.True);
    }
}
