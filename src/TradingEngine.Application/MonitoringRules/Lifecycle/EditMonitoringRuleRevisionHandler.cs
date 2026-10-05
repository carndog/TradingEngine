using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed class EditMonitoringRuleRevisionHandler
{
    private readonly IMonitoringRuleStore _store;
    private readonly IClock _clock;

    public EditMonitoringRuleRevisionHandler(
        IMonitoringRuleStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<MonitoringRuleSnapshot>> HandleAsync(
        EditMonitoringRuleRevision command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Instant now = _clock.GetCurrentInstant();

        (Instant? from, Error? fromError) = MonitoringRuleCommandSupport
            .RequirePersistable(command.EffectiveFrom);
        if (fromError is not null)
        {
            return fromError;
        }

        return await MonitoringRuleCommandSupport.MutateAsync(
            _store,
            command.InstrumentId,
            command.ExpectedConcurrencyToken,
            now,
            false,
            (rule, captured) => ApplyEdits(rule, command, captured, from),
            cancellationToken);
    }

    private static Result ApplyEdits(
        MonitoringRule rule,
        EditMonitoringRuleRevision command,
        Instant captured,
        Instant? effectiveFrom)
    {
        if (command.Definition is not null)
        {
            Result edited = rule.EditScheduledRevision(
                command.RevisionId,
                command.Definition,
                command.ChangeReason,
                captured);
            if (edited.IsFailure)
            {
                return edited;
            }
        }

        return effectiveFrom is not null ? rule.Reschedule(command.RevisionId, effectiveFrom.Value, captured) : Result.Success();
    }
}
