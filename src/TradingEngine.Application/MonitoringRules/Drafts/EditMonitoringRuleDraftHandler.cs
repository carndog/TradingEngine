using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules.Drafts;

public sealed class EditMonitoringRuleDraftHandler
{
    private readonly IMonitoringRuleStore _store;
    private readonly IClock _clock;

    public EditMonitoringRuleDraftHandler(
        IMonitoringRuleStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<MonitoringRuleSnapshot>> HandleAsync(
        EditMonitoringRuleDraft command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Instant now = _clock.GetCurrentInstant();

        (Instant? proposedFrom, Error? fromError) = MonitoringRuleCommandSupport
            .RequirePersistable(command.Proposal?.EffectiveFrom);
        if (fromError is not null)
        {
            return fromError;
        }

        (Instant? proposedTo, Error? toError) = MonitoringRuleCommandSupport
            .RequirePersistable(command.Proposal?.EffectiveTo);
        if (toError is not null)
        {
            return toError;
        }

        Domain.Revisions.RevisionProposal? proposal = command.Proposal is null
            ? null
            : new Domain.Revisions.RevisionProposal(
                proposedFrom,
                proposedTo);

        return await MonitoringRuleCommandSupport.MutateAsync(
            _store,
            command.InstrumentId,
            command.ExpectedConcurrencyToken,
            now,
            false,
            (rule, _) => rule.EditDraft(
                command.DraftId,
                command.Definition,
                command.ChangeReason,
                proposal),
            cancellationToken);
    }
}
