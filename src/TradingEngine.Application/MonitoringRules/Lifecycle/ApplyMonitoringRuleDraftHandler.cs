using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed class ApplyMonitoringRuleDraftHandler
{
    private readonly IMonitoringRuleStore _store;
    private readonly IClock _clock;

    public ApplyMonitoringRuleDraftHandler(
        IMonitoringRuleStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<MonitoringRuleSnapshot>> HandleAsync(
        ApplyMonitoringRuleDraft command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Instant now = _clock.GetCurrentInstant();

        (Instant? to, Error? toError) = MonitoringRuleCommandSupport
            .RequirePersistable(command.EffectiveTo);
        if (toError is not null)
        {
            return toError;
        }

        return await MonitoringRuleCommandSupport.MutateAsync(
            _store,
            command.InstrumentId,
            command.ExpectedConcurrencyToken,
            now,
            true,
            (rule, captured) => rule.ApplyNow(
                command.DraftId,
                to,
                Guid.NewGuid(),
                captured),
            cancellationToken);
    }
}
