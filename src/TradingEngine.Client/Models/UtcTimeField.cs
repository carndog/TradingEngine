using TradingEngine.Client.Services;

namespace TradingEngine.Client.Models;

public sealed class UtcTimeField
{
    public UtcTimeField(DateTimeOffset? baseline)
    {
        Baseline = baseline;
        Text = UtcTimes.ToInputValue(baseline);
    }

    public DateTimeOffset? Baseline { get; private set; }

    public bool HasSubMillisecondBaseline => UtcTimes.HasSubMillisecondTicks(Baseline);

    public string BaselinePreciseLabel => Baseline is null
        ? string.Empty
        : UtcTimes.FormatPrecise(Baseline.Value);

    public string Text { get; private set; }

    public bool Modified { get; private set; }

    public void Set(string? value)
    {
        Text = value ?? string.Empty;
        Modified = true;
    }

    public void Reseed(DateTimeOffset? baseline)
    {
        Baseline = baseline;
        Text = UtcTimes.ToInputValue(baseline);
        Modified = false;
    }

    public void AcceptResolved()
    {
        UtcTimeResolution resolved = Resolve();
        if (resolved.Kind != UtcTimeResolutionKind.Invalid)
        {
            Baseline = resolved.Value;
            Modified = false;
        }
    }

    public UtcTimeResolution Resolve()
    {
        if (Modified is false)
        {
            return new UtcTimeResolution(UtcTimeResolutionKind.Unmodified, Baseline);
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            return new UtcTimeResolution(
                Baseline is null ? UtcTimeResolutionKind.Unmodified : UtcTimeResolutionKind.Cleared,
                null);
        }

        if (UtcTimes.TryParseInput(Text, out DateTimeOffset? value) is false)
        {
            return new UtcTimeResolution(UtcTimeResolutionKind.Invalid, null);
        }

        return new UtcTimeResolution(
            value == Baseline ? UtcTimeResolutionKind.Unmodified : UtcTimeResolutionKind.Valid,
            value);
    }

    public ApiError? RequestOptional(out string? isoUtc)
    {
        UtcTimeResolution resolved = Resolve();
        if (resolved.Kind == UtcTimeResolutionKind.Invalid)
        {
            isoUtc = null;
            return InvalidError();
        }

        isoUtc = resolved.Kind == UtcTimeResolutionKind.Cleared
            ? null
            : UtcTimes.ToIsoUtc(resolved.Value);
        return null;
    }

    public ApiError? RequestRequired(out string? isoUtc)
    {
        UtcTimeResolution resolved = Resolve();
        switch (resolved.Kind)
        {
            case UtcTimeResolutionKind.Invalid:
                isoUtc = null;
                return InvalidError();
            case UtcTimeResolutionKind.Cleared:
                isoUtc = null;
                return RequiredError();
            default:
                isoUtc = UtcTimes.ToIsoUtc(resolved.Value);
                return resolved.Value is null ? RequiredError() : null;
        }
    }

    public ApiError? RequestKeepUnchanged(out string? isoUtc)
    {
        UtcTimeResolution resolved = Resolve();
        if (resolved.Kind == UtcTimeResolutionKind.Valid)
        {
            isoUtc = UtcTimes.ToIsoUtc(resolved.Value);
            return null;
        }

        isoUtc = null;
        return resolved.Kind switch
        {
            UtcTimeResolutionKind.Invalid => InvalidError(),
            UtcTimeResolutionKind.Cleared => RequiredError(),
            _ => null
        };
    }

    private static ApiError InvalidError()
    {
        return ApiError.Create(400, "input.invalid_time",
            "The value is not a valid UTC date and time.", null);
    }

    private static ApiError RequiredError()
    {
        return ApiError.Create(400, "input.required",
            "A UTC date and time is required.", null);
    }
}
