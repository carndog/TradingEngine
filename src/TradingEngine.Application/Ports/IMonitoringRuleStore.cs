using TradingEngine.Application.MonitoringRules;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface IMonitoringRuleStore
{
    Task<Result<MonitoringRuleSnapshot>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken);

    Task<Result> AddAsync(
        MonitoringRule rule,
        CancellationToken cancellationToken);

    Task<Result> SaveAsync(
        MonitoringRuleSnapshot snapshot,
        CancellationToken cancellationToken);
}
