using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.Recommendations;

internal sealed class StubMonitoringRuleStore : IMonitoringRuleStore
{
    public Dictionary<Guid, Result<MonitoringRuleSnapshot>> Snapshots { get; } = [];

    public int GetCalls { get; private set; }

    public Task<Result<MonitoringRuleSnapshot>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        GetCalls++;
        if (Snapshots.TryGetValue(
                instrumentId,
                out Result<MonitoringRuleSnapshot>? snapshot)
            && snapshot is not null)
        {
            return Task.FromResult(snapshot);
        }

        return Task.FromResult<Result<MonitoringRuleSnapshot>>(
            MonitoringRuleErrors.NotFound);
    }

    public Task<Result> AddAsync(
        MonitoringRule rule,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<Result> SaveAsync(
        MonitoringRuleSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
