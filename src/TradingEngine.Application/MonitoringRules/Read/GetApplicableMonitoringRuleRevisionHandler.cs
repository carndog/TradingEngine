using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Application.MonitoringRules.Read;

public sealed class GetApplicableMonitoringRuleRevisionHandler
{
    private readonly IMonitoringRuleStore _store;
    private readonly IClock _clock;

    public GetApplicableMonitoringRuleRevisionHandler(
        IMonitoringRuleStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<MonitoringRuleApplicableRevision>> HandleAsync(
        GetApplicableMonitoringRuleRevision query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Instant now = _clock.GetCurrentInstant();
        Instant at = query.At ?? now;

        Result<MonitoringRuleSnapshot> snapshot = await _store.GetAsync(
            query.InstrumentId,
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return snapshot.Error;
        }

        Revision<ChartAnalysisDefinition>? applicable = snapshot.Value.Rule.EffectiveAt(at);
        if (applicable is null)
        {
            return MonitoringRuleErrors.NoApplicableRevision;
        }

        return new MonitoringRuleApplicableRevision(snapshot.Value, applicable);
    }
}
