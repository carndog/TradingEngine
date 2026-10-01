using NodaTime;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Revisions;

public sealed record EffectivePeriod
{
    private EffectivePeriod(Instant effectiveFrom, Instant? effectiveTo)
    {
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public Instant EffectiveFrom { get; }

    public Instant? EffectiveTo { get; }

    public bool IsOpenEnded => EffectiveTo is null;

    public static Result<EffectivePeriod> Create(Instant effectiveFrom, Instant? effectiveTo)
    {
        if (effectiveTo is not null && effectiveTo.Value <= effectiveFrom)
        {
            return RevisionErrors.InvalidPeriod;
        }

        return new EffectivePeriod(effectiveFrom, effectiveTo);
    }

    public bool Contains(Instant instant)
    {
        return instant >= EffectiveFrom && (EffectiveTo is null || instant < EffectiveTo.Value);
    }

    public bool HasBegun(Instant now)
    {
        return now >= EffectiveFrom;
    }

    public bool Overlaps(EffectivePeriod other)
    {
        ArgumentNullException.ThrowIfNull(other);

        bool thisEndsBeforeOther = EffectiveTo is not null && EffectiveTo.Value <= other.EffectiveFrom;
        bool otherEndsBeforeThis = other.EffectiveTo is not null && other.EffectiveTo.Value <= EffectiveFrom;

        return thisEndsBeforeOther is false && otherEndsBeforeThis is false;
    }
}
