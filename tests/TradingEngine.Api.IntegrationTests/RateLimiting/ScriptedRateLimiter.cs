using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

internal sealed class ScriptedRateLimiter : PartitionedRateLimiter<HttpContext>
{
    private readonly IReadOnlyList<RateLimitLease> _leases;
    private int _index = -1;

    public ScriptedRateLimiter(params RateLimitLease[] leases)
    {
        ArgumentNullException.ThrowIfNull(leases);
        _leases = leases;
    }

    public override RateLimiterStatistics? GetStatistics(HttpContext resourceID)
    {
        return null;
    }

    protected override RateLimitLease AttemptAcquireCore(
        HttpContext resourceID,
        int permitCount)
    {
        return Next();
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpContext resourceID,
        int permitCount,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Next());
    }

    private RateLimitLease Next()
    {
        int index = Interlocked.Increment(ref _index);

        return _leases[Math.Min(index, _leases.Count - 1)];
    }
}
