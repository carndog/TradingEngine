using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules.Read;

public sealed class GetMonitoringRuleTimelineHandler
{
    private readonly IMonitoringRuleStore _store;

    public GetMonitoringRuleTimelineHandler(IMonitoringRuleStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<MonitoringRuleSnapshot>> HandleAsync(
        GetMonitoringRuleTimeline query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _store.GetAsync(query.InstrumentId, cancellationToken);
    }
}
