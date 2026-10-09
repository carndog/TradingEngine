using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using TradingEngine.Api.RateLimiting;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

internal sealed class ScriptedAdministrationRequestLimiter : IAdministrationRequestLimiter
{
    private readonly IReadOnlyList<RateLimitLease> _leases;
    private int _index = -1;

    public ScriptedAdministrationRequestLimiter(params RateLimitLease[] leases)
    {
        ArgumentNullException.ThrowIfNull(leases);
        _leases = leases;
    }

    public ValueTask<RateLimitLease> AcquireAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        int index = Interlocked.Increment(ref _index);

        return ValueTask.FromResult(_leases[Math.Min(index, _leases.Count - 1)]);
    }
}
