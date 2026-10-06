using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed class RemoveMonitoringRuleRevisionHandler
{
    private readonly IMonitoringRuleStore _store;
    private readonly IClock _clock;

    public RemoveMonitoringRuleRevisionHandler(
        IMonitoringRuleStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<MonitoringRuleSnapshot>> HandleAsync(
        RemoveMonitoringRuleRevision command,
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
            (rule, captured) => rule.RemoveScheduledRevision(
                command.RevisionId,
                captured),
            cancellationToken);
    }
}
