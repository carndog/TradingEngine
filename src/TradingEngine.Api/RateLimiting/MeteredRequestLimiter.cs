using System.Threading.RateLimiting;

namespace TradingEngine.Api.RateLimiting;

internal sealed class MeteredRequestLimiter : PartitionedRateLimiter<HttpContext>
{
    private readonly PartitionedRateLimiter<HttpContext> _inner;
    private readonly RateLimitingTelemetry _telemetry;

    public MeteredRequestLimiter(
        PartitionedRateLimiter<HttpContext> inner,
        RateLimitingTelemetry telemetry)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public override RateLimiterStatistics? GetStatistics(HttpContext resourceID)
    {
        return _inner.GetStatistics(resourceID);
    }

    protected override RateLimitLease AttemptAcquireCore(
        HttpContext resourceID,
        int permitCount)
    {
        RateLimitLease lease = _inner.AttemptAcquire(resourceID, permitCount);
        if (lease.IsAcquired)
        {
            _telemetry.RecordAccepted(RateLimitPolicies.ForRequest(resourceID));
        }

        return lease;
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpContext resourceID,
        int permitCount,
        CancellationToken cancellationToken)
    {
        try
        {
            RateLimitLease lease = await _inner.AcquireAsync(
                resourceID, permitCount, cancellationToken);
            if (lease.IsAcquired)
            {
                _telemetry.RecordAccepted(RateLimitPolicies.ForRequest(resourceID));
            }

            return lease;
        }
        catch (OperationCanceledException)
        {
            _telemetry.RecordCancelled(RateLimitPolicies.ForRequest(resourceID));
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _inner.DisposeAsync();
    }
}
