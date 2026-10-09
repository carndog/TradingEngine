using System.Threading.RateLimiting;

namespace TradingEngine.Api.RateLimiting;

internal sealed class MeteredRateLimiter : RateLimiter
{
    private readonly RateLimiter _inner;
    private readonly RateLimitingTelemetry _telemetry;
    private readonly string _policy;

    public MeteredRateLimiter(RateLimiter inner, RateLimitingTelemetry telemetry, string policy)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        _policy = policy;
    }

    public override TimeSpan? IdleDuration => _inner.IdleDuration;

    public override RateLimiterStatistics? GetStatistics()
    {
        return _inner.GetStatistics();
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        RateLimitLease lease = _inner.AttemptAcquire(permitCount);
        _telemetry.Record(_policy, lease.IsAcquired);

        return lease;
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount,
        CancellationToken cancellationToken)
    {
        RateLimitLease lease = await _inner.AcquireAsync(permitCount, cancellationToken);
        _telemetry.Record(_policy, lease.IsAcquired);

        return lease;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        return _inner.DisposeAsync();
    }
}
