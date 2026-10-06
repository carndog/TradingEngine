using NodaTime;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Domain.Tests.Revisions;

public static class SyntheticTimelines
{
    public const string Author = "synthetic-user";

    public static Instant October(int day, int hour = 0)
    {
        return Instant.FromUtc(2026, 10, day, hour, 0);
    }

    public static Guid Id(int ordinal)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{ordinal:D12}");
    }

    public static SyntheticLimitDefinition Limit(int limit)
    {
        return new SyntheticLimitDefinition(limit, ["synthetic"]);
    }

    public static RevisionTimeline<SyntheticLimitDefinition> Empty()
    {
        return RevisionTimeline<SyntheticLimitDefinition>.Restore(
            Array.Empty<RestoredRevision<SyntheticLimitDefinition>>()).Value;
    }

    public static RevisionTimeline<SyntheticLimitDefinition> Timeline(
        int ordinal,
        int limit,
        Instant createdAt,
        string? createdBy = Author)
    {
        return RevisionTimeline<SyntheticLimitDefinition>.Create(
            Id(ordinal),
            Limit(limit),
            createdAt,
            createdBy).Value;
    }

    public static Revision<SyntheticLimitDefinition> Draft(
        RevisionTimeline<SyntheticLimitDefinition> timeline,
        int ordinal,
        int limit,
        Instant createdAt,
        RevisionProposal? proposal = null)
    {
        return timeline.CreateDraft(Id(ordinal), Limit(limit), createdAt, Author, null, proposal).Value;
    }

    public static Revision<SyntheticLimitDefinition> Apply(
        RevisionTimeline<SyntheticLimitDefinition> timeline,
        int ordinal,
        int limit,
        Instant now,
        int continuationOrdinal = 900)
    {
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, ordinal, limit, now);
        timeline.ApplyNow(draft.Id, Id(continuationOrdinal), now);

        return draft;
    }

    public static Revision<SyntheticLimitDefinition> Schedule(
        RevisionTimeline<SyntheticLimitDefinition> timeline,
        int ordinal,
        int limit,
        Instant effectiveFrom,
        Instant now,
        Instant? effectiveTo = null,
        int continuationOrdinal = 900)
    {
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, ordinal, limit, now);
        timeline.Schedule(draft.Id, effectiveFrom, effectiveTo, Id(continuationOrdinal), now);

        return draft;
    }
}
