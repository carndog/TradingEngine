using TradingEngine.Client.Models;
using TradingEngine.Client.Services;

namespace TradingEngine.Client.Tests.Models;

[TestFixture]
public sealed class UtcTimeFieldTests
{
    private static readonly DateTimeOffset Baseline =
        DateTimeOffset.Parse("2026-10-07T10:11:12.1234567Z");

    [Test]
    public void Resolve_UnmodifiedField_ReturnsUnmodifiedWithBaseline()
    {
        UtcTimeField field = new(Baseline);

        UtcTimeResolution resolved = field.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Kind, Is.EqualTo(UtcTimeResolutionKind.Unmodified));
            Assert.That(resolved.Value, Is.EqualTo(Baseline));
        });
    }

    [Test]
    public void Resolve_ClearedText_ReturnsCleared()
    {
        UtcTimeField field = new(Baseline);
        field.Set(string.Empty);

        UtcTimeResolution resolved = field.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Kind, Is.EqualTo(UtcTimeResolutionKind.Cleared));
            Assert.That(resolved.Value, Is.Null);
        });
    }

    [Test]
    public void Resolve_InvalidText_ReturnsInvalid()
    {
        UtcTimeField field = new(Baseline);
        field.Set("not-a-date");

        UtcTimeResolution resolved = field.Resolve();

        Assert.That(resolved.Kind, Is.EqualTo(UtcTimeResolutionKind.Invalid));
    }

    [Test]
    public void Resolve_ValidText_ReturnsParsedUtc()
    {
        UtcTimeField field = new(null);
        field.Set("2026-11-01T08:30:00");

        UtcTimeResolution resolved = field.Resolve();

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Kind, Is.EqualTo(UtcTimeResolutionKind.Valid));
            Assert.That(resolved.Value, Is.EqualTo(DateTimeOffset.Parse("2026-11-01T08:30:00Z")));
        });
    }

    [Test]
    public void RequestOptional_UnmodifiedFractionalBaseline_SerializesExactInstant()
    {
        UtcTimeField field = new(Baseline);

        ApiError? error = field.RequestOptional(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(isoUtc, Is.EqualTo("2026-10-07T10:11:12.1234567Z"));
        });
    }

    [Test]
    public void RequestOptional_ClearedField_ReturnsNullWithoutError()
    {
        UtcTimeField field = new(Baseline);
        field.Set("   ");

        ApiError? error = field.RequestOptional(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(isoUtc, Is.Null);
        });
    }

    [Test]
    public void RequestOptional_InvalidText_ReturnsValidationError()
    {
        UtcTimeField field = new(Baseline);
        field.Set("tomorrow-ish");

        ApiError? error = field.RequestOptional(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Code, Is.EqualTo("input.invalid_time"));
            Assert.That(isoUtc, Is.Null);
        });
    }

    [Test]
    public void RequestRequired_ClearedField_ReturnsRequiredError()
    {
        UtcTimeField field = new(null);
        field.Set(string.Empty);

        ApiError? error = field.RequestRequired(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Code, Is.EqualTo("input.required"));
            Assert.That(isoUtc, Is.Null);
        });
    }

    [Test]
    public void RequestRequired_UnmodifiedNullBaseline_ReturnsRequiredError()
    {
        UtcTimeField field = new(null);

        ApiError? error = field.RequestRequired(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(isoUtc, Is.Null);
        });
    }

    [Test]
    public void RequestKeepUnchanged_UnmodifiedField_ReturnsNullIso()
    {
        UtcTimeField field = new(Baseline);

        ApiError? error = field.RequestKeepUnchanged(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(isoUtc, Is.Null);
        });
    }

    [Test]
    public void RequestKeepUnchanged_ClearedField_ReturnsRequiredError()
    {
        UtcTimeField field = new(Baseline);
        field.Set(string.Empty);

        ApiError? error = field.RequestKeepUnchanged(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Code, Is.EqualTo("input.required"));
            Assert.That(isoUtc, Is.Null);
        });
    }

    [Test]
    public void Resolve_SameValueTyped_ReturnsUnmodified()
    {
        UtcTimeField field = new(Baseline);
        field.Set("2026-10-07T10:11:12.1234567");

        UtcTimeResolution resolved = field.Resolve();

        Assert.That(resolved.Kind, Is.EqualTo(UtcTimeResolutionKind.Unmodified));
    }

    [Test]
    public void Resolve_ClearedTextOnNullBaseline_ReturnsUnmodified()
    {
        UtcTimeField field = new(null);
        field.Set(string.Empty);

        UtcTimeResolution resolved = field.Resolve();

        Assert.That(resolved.Kind, Is.EqualTo(UtcTimeResolutionKind.Unmodified));
    }

    [Test]
    public void RequestKeepUnchanged_ValidText_ReturnsParsedIso()
    {
        UtcTimeField field = new(Baseline);
        field.Set("2026-10-09T00:00:00");

        ApiError? error = field.RequestKeepUnchanged(out string? isoUtc);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(isoUtc, Is.EqualTo("2026-10-09T00:00:00.0000000Z"));
        });
    }

    [Test]
    public void AcceptResolved_ModifiedField_BecomesUnmodifiedWithParsedBaseline()
    {
        UtcTimeField field = new(null);
        field.Set("2026-11-01T08:30:00");

        field.AcceptResolved();

        Assert.Multiple(() =>
        {
            Assert.That(field.Modified, Is.False);
            Assert.That(field.Baseline, Is.EqualTo(DateTimeOffset.Parse("2026-11-01T08:30:00Z")));
        });
    }

    [Test]
    public void AcceptResolved_InvalidText_KeepsModifiedFlag()
    {
        UtcTimeField field = new(Baseline);
        field.Set("nope");

        field.AcceptResolved();

        Assert.That(field.Modified, Is.True);
    }
}

