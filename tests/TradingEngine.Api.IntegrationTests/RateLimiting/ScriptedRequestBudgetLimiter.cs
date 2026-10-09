using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using TradingEngine.Api.RateLimiting;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

internal sealed class ScriptedRequestBudgetLimiter : IRequestBudgetLimiter
{
    public ScriptedRequestBudgetLimiter(params RateLimitLease[] leases)
    {
        Limiter = new ScriptedRateLimiter(leases);
    }

    public PartitionedRateLimiter<HttpContext> Limiter { get; }
}
