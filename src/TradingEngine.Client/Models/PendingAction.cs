using TradingEngine.Client.Services;
using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Models;

public sealed class PendingAction
{
    public PendingAction(PendingActionKind kind, MonitoringRuleRevisionDto revision, string capturedToken)
    {
        Kind = kind;
        Revision = revision;
        CapturedToken = capturedToken;
        ProposedEffectiveFrom = revision.ProposedEffectiveFrom;
    }

    public PendingActionKind Kind { get; }

    public MonitoringRuleRevisionDto Revision { get; }

    public string CapturedToken { get; set; }

    public UtcTimeField? From { get; set; }

    public UtcTimeField? To { get; set; }

    public DateTimeOffset? ProposedEffectiveFrom { get; set; }

    public bool ProposalReplacementAcknowledged { get; set; }

    public bool RequiresProposalAcknowledgement =>
        Kind == PendingActionKind.Apply && ProposedEffectiveFrom is not null;

    public bool HasInput =>
        From?.Modified == true || To?.Modified == true || ProposalReplacementAcknowledged;

    public ApiError? ResolveInputs(
        DateTimeOffset now,
        out string? effectiveFromIso,
        out string? effectiveToIso)
    {
        effectiveFromIso = null;
        effectiveToIso = null;

        if (Kind == PendingActionKind.Schedule)
        {
            ApiError? fromError = From!.RequestRequired(out effectiveFromIso);
            if (fromError is not null)
            {
                return fromError;
            }

            DateTimeOffset? start = From.Resolve().Value;
            if (start is not null && start.Value < now)
            {
                return ApiError.Create(400, "schedule.effective_from_past",
                    $"Effective from {UtcTimes.Format(start.Value)} is already in the past (now {UtcTimes.Format(now)}). "
                    + "Choose a future start, or use Apply now to start at the server's current time. The timeline was not changed.",
                    null);
            }
        }

        if (Kind is PendingActionKind.Apply or PendingActionKind.Schedule)
        {
            ApiError? toError = To!.RequestOptional(out effectiveToIso);
            if (toError is not null)
            {
                return toError;
            }
        }

        if (RequiresProposalAcknowledgement && ProposalReplacementAcknowledged is false)
        {
            return ApiError.Create(400, "apply.proposal_unacknowledged",
                $"This draft proposes to start at {UtcTimes.Format(ProposedEffectiveFrom!.Value)}. "
                + "Applying now ignores that date and starts at the server's current time. "
                + "Tick the acknowledgement to proceed, or edit the draft to clear or correct the proposed start.",
                null);
        }

        return null;
    }
}
