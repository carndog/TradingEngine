using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace TradingEngine.Api.RateLimiting;

internal sealed class RequestBudgetLimiter : IRequestBudgetLimiter, IDisposable, IAsyncDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    public RequestBudgetLimiter(IOptions<ApiRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ApiRateLimitOptions limits = options.Value;

        _limiter = PartitionedRateLimiter.Create<HttpContext, (string Policy, string Caller)>(
            context =>
            {
                string policy = RateLimitPolicies.ForRequest(context);
                (string Policy, string Caller) partition = (
                    policy,
                    policy == RateLimitPolicies.DatabaseProbe
                        ? string.Empty
                        : RateLimitPolicies.ResolveCaller(context));

                return RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    key => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimit(limits, key.Policy),
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = limits.QueueLimit
                    });
            });
    }

    public PartitionedRateLimiter<HttpContext> Limiter => _limiter;

    public void Dispose()
    {
        _limiter.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _limiter.DisposeAsync();
    }

    private static int PermitLimit(ApiRateLimitOptions limits, string policy)
    {
        if (policy == RateLimitPolicies.DatabaseProbe)
        {
            return limits.DatabaseProbePermitLimit;
        }

        return policy == RateLimitPolicies.AdminWrite
            ? limits.WritePermitLimit
            : limits.ReadPermitLimit;
    }
}
