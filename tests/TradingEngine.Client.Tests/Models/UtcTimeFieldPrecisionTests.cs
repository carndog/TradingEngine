using TradingEngine.Client.Models;
using TradingEngine.Client.Services;

namespace TradingEngine.Client.Tests.Models;

[TestFixture]
public sealed class UtcTimeFieldPrecisionTests
{
    private static readonly DateTimeOffset MillisecondBaseline =
        DateTimeOffset.Parse("2026-10-07T10:11:12.1230000Z");
    private static readonly DateTimeOffset SubMillisecondBaseline =
        DateTimeOffset.Parse("2026-10-07T10:11:12.1234567Z");

    [Test]
    public void Constructor_SubMillisecondBaseline_TextHoldsValidControlValue()
    {
        UtcTimeField field = new(SubMillisecondBaseline);

        Assert.Multiple(() =>
        {
            Assert.That(field.Text, Is.EqualTo("2026-10-07T10:11:12.123"));
            Assert.That(field.HasSubMillisecondBaseline, Is.True);
            Assert.That(field.BaselinePreciseLabel, Does.Contain(".1234567"));
            Assert.That(field.Baseline, Is.EqualTo(SubMillisecondBaseline));
        });
    }

    [Test]
    public void Constructor_MillisecondBaseline_TextMatchesStoredValue()
    {
        UtcTimeField field = new(MillisecondBaseline);

        Assert.Multiple(() =>
        {
            Assert.That(field.Text, Is.EqualTo("2026-10-07T10:11:12.123"));
            Assert.That(field.HasSubMillisecondBaseline, Is.False);
        });
    }

    [Test]
    public void RequestRequired_UnmodifiedSubMillisecondBaseline_EmitsExactTicks()
    {
        UtcTimeField field = new(SubMillisecondBaseline);

        ApiError? error = field.RequestRequired(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(isoUtc, Is.EqualTo("2026-10-07T10:11:12.1234567Z"));
        });
    }

    [Test]
    public void RequestOptional_PreciseTextEntry_ParsesSevenFractionalDigits()
    {
        UtcTimeField field = new(MillisecondBaseline);
        field.Set("2026-10-07T10:11:12.1234567");

        ApiError? error = field.RequestOptional(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(isoUtc, Is.EqualTo("2026-10-07T10:11:12.1234567Z"));
        });
    }

    [Test]
    public void Resolve_SubMillisecondBaselineWithControlText_TreatedAsModifiedToMillisecond()
    {
        UtcTimeField field = new(SubMillisecondBaseline);
        field.Set(field.Text);

        UtcTimeResolution resolved = field.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Kind, Is.EqualTo(UtcTimeResolutionKind.Valid));
            Assert.That(
                resolved.Value,
                Is.EqualTo(DateTimeOffset.Parse("2026-10-07T10:11:12.1230000Z")));
        });
    }
}
