using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules.Drafts;

public sealed class DeleteMonitoringRuleDraftHandler
{
    private readonly IMonitoringRuleStore _store;
    private readonly IClock _clock;

    public DeleteMonitoringRuleDraftHandler(
        IMonitoringRuleStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<MonitoringRuleSnapshot>> HandleAsync(
        DeleteMonitoringRuleDraft command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Instant now = _clock.GetCurrentInstant();

        return await MonitoringRuleCommandSupport.MutateAsync(
            _store,
            command.InstrumentId,
            command.ExpectedConcurrencyToken,
            now,
            false,
            (rule, _) => rule.DeleteDraft(command.DraftId),
            cancellationToken);
    }
}
