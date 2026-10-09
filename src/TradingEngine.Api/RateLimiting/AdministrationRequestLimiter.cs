using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace TradingEngine.Api.RateLimiting;

internal sealed class AdministrationRequestLimiter :
    IAdministrationRequestLimiter, IDisposable, IAsyncDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _callerBudgets;

    public AdministrationRequestLimiter(IOptions<ApiRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ApiRateLimitOptions limits = options.Value;

        _callerBudgets = PartitionedRateLimiter.Create<HttpContext, (string Caller, bool Write)>(
            context =>
            {
                (string Caller, bool Write) partition = (
                    RateLimitPolicies.ResolveCaller(context),
                    RateLimitPolicies.IsWrite(context.Request.Method));

                return RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    key => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = key.Write
                            ? limits.WritePermitLimit
                            : limits.ReadPermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = limits.QueueLimit
                    });
            });
    }

    public ValueTask<RateLimitLease> AcquireAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return _callerBudgets.AcquireAsync(context, 1, cancellationToken);
    }

    public void Dispose()
    {
        _callerBudgets.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _callerBudgets.DisposeAsync();
    }
}
