using NodaTime;
using TradingEngine.Application.Time;

namespace TradingEngine.Application.Tests.Time;

[TestFixture]
public sealed class PersistedInstantTests
{
    private static readonly Instant Boundary = Instant.FromUtc(2026, 10, 5, 12, 0);

    [Test]
    public void Require_WithWholeSecondInstant_ReturnsSameInstant()
    {
        Instant result = PersistedInstant.Require(Boundary, "value");

        Assert.That(result, Is.EqualTo(Boundary));
    }

    [Test]
    public void Require_WithTickAlignedFractionalSeconds_ReturnsSameInstant()
    {
        Instant instant = Boundary.PlusNanoseconds(123_456_700);

        Instant result = PersistedInstant.Require(instant, "value");

        Assert.That(result, Is.EqualTo(instant));
    }

    [Test]
    public void Require_AtSupportedRangeLimits_ReturnsSameInstant()
    {
        Instant minimum = Instant.FromDateTimeUtc(
            DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        Instant maximum = Instant.FromDateTimeUtc(
            DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

        Assert.Multiple(() =>
        {
            Assert.That(PersistedInstant.Require(minimum, "value"), Is.EqualTo(minimum));
            Assert.That(PersistedInstant.Require(maximum, "value"), Is.EqualTo(maximum));
        });
    }

    [Test]
    public void Require_BelowSupportedRange_Throws()
    {
        Assert.That(
            () => PersistedInstant.Require(Instant.MinValue, "value"),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Require_AboveSupportedRange_Throws()
    {
        Assert.That(
            () => PersistedInstant.Require(Instant.MaxValue, "value"),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(1L)]
    [TestCase(50L)]
    [TestCase(99L)]
    public void Require_WithSubTickPrecision_Throws(long nanoseconds)
    {
        Instant instant = Boundary.PlusNanoseconds(nanoseconds);

        Assert.That(
            () => PersistedInstant.Require(instant, "value"),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Require_WithNullNullableInstant_ReturnsNull()
    {
        Instant? result = PersistedInstant.Require((Instant?)null, "value");

        Assert.That(result, Is.Null);
    }
}
