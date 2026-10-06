using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed class ScheduleMonitoringRuleDraftHandler
{
    private readonly IMonitoringRuleStore _store;
    private readonly IClock _clock;

    public ScheduleMonitoringRuleDraftHandler(
        IMonitoringRuleStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<MonitoringRuleSnapshot>> HandleAsync(
        ScheduleMonitoringRuleDraft command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Instant now = _clock.GetCurrentInstant();

        Result<Instant> from = MonitoringRuleCommandSupport
            .RequirePersistable(command.EffectiveFrom);
        if (from.IsFailure)
        {
            return from.Error;
        }

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
            (rule, captured) => rule.Schedule(
                command.DraftId,
                from.Value,
                to,
                Guid.NewGuid(),
                captured),
            cancellationToken);
    }
}
