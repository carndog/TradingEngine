using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

internal sealed class StubMonitoringRuleStore : IMonitoringRuleStore
{
    private byte _version = 1;
    private byte[] _token = [1];

    public MonitoringRule? Rule { get; private set; }

    public string CurrentToken => Convert.ToBase64String(_token);

    public void Seed(MonitoringRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Rule = rule;
    }

    public Task<Result<MonitoringRuleSnapshot>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Rule is null)
        {
            return Task.FromResult<Result<MonitoringRuleSnapshot>>(MonitoringRuleErrors.NotFound);
        }

        return Task.FromResult(
            Result<MonitoringRuleSnapshot>.Success(new MonitoringRuleSnapshot(Rule, _token)));
    }

    public Task<Result> AddAsync(
        MonitoringRule rule,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Rule = rule;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SaveAsync(
        MonitoringRuleSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.ConcurrencyToken.SequenceEqual(_token) is false)
        {
            return Task.FromResult<Result>(MonitoringRuleErrors.ConcurrentChange);
        }

        _version++;
        _token = [_version];
        return Task.FromResult(Result.Success());
    }
}
