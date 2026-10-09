using NodaTime;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

internal sealed class StubMonitoringRuleStore : IMonitoringRuleStore
{
    private Guid _ruleId;
    private Guid _instrumentId;
    private Instant _createdAt;
    private IReadOnlyList<RestoredRevision<ChartAnalysisDefinition>> _persisted = [];
    private byte _version = 1;
    private byte[] _token = [1];
    private int _getCount;
    private int _activeReads;
    private int _maxActiveReads;

    public int SaveCount { get; private set; }

    public int GetCount => _getCount;

    public int ActiveReads => _activeReads;

    public int MaxActiveReads => _maxActiveReads;

    public Task? ReadGate { get; set; }

    public bool ThrowOnRead { get; set; }

    public string CurrentToken => Convert.ToBase64String(_token);

    public void Seed(MonitoringRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Publish(rule);
    }

    public MonitoringRule Load()
    {
        return Restore();
    }

    public async Task<Result<MonitoringRuleSnapshot>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _getCount);
        TrackReadStart();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Synthetic store failure.");
            }

            Task? gate = ReadGate;
            if (gate is not null)
            {
                await gate.WaitAsync(cancellationToken);
            }

            if (_ruleId == Guid.Empty || instrumentId != _instrumentId)
            {
                return MonitoringRuleErrors.NotFound;
            }

            return Result<MonitoringRuleSnapshot>.Success(
                new MonitoringRuleSnapshot(Restore(), _token));
        }
        finally
        {
            Interlocked.Decrement(ref _activeReads);
        }
    }

    public Task<Result> AddAsync(
        MonitoringRule rule,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Publish(rule);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SaveAsync(
        MonitoringRuleSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.Rule.Id != _ruleId || snapshot.Rule.WatchedInstrumentId != _instrumentId)
        {
            return Task.FromResult<Result>(MonitoringRuleErrors.NotFound);
        }

        if (snapshot.ConcurrencyToken.SequenceEqual(_token) is false)
        {
            return Task.FromResult<Result>(MonitoringRuleErrors.ConcurrentChange);
        }

        Publish(snapshot.Rule);
        _version++;
        _token = [_version];
        SaveCount++;
        return Task.FromResult(Result.Success());
    }

    private void TrackReadStart()
    {
        int active = Interlocked.Increment(ref _activeReads);
        int observed = _maxActiveReads;
        while (active > observed)
        {
            observed = Interlocked.CompareExchange(ref _maxActiveReads, active, observed);
        }
    }

    private void Publish(MonitoringRule rule)
    {
        _ruleId = rule.Id;
        _instrumentId = rule.WatchedInstrumentId;
        _createdAt = rule.CreatedAt;
        _persisted = rule.Revisions.Concat(rule.Drafts).Select(Capture).ToArray();
    }

    private MonitoringRule Restore()
    {
        return MonitoringRule.Restore(_ruleId, _instrumentId, _createdAt, _persisted).Value;
    }

    private static RestoredRevision<ChartAnalysisDefinition> Capture(
        Revision<ChartAnalysisDefinition> revision)
    {
        return new RestoredRevision<ChartAnalysisDefinition>(
            revision.Id,
            revision.Definition,
            revision.CreatedAt,
            revision.CreatedBy,
            revision.ChangeReason,
            revision.RevisionNumber,
            revision.EffectivePeriod?.EffectiveFrom,
            revision.EffectivePeriod?.EffectiveTo,
            revision.Proposal?.EffectiveFrom,
            revision.Proposal?.EffectiveTo);
    }
}
