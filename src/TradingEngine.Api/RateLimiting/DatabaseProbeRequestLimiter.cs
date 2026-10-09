using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace TradingEngine.Api.RateLimiting;

internal sealed class DatabaseProbeRequestLimiter :
    IDatabaseProbeRequestLimiter, IDisposable, IAsyncDisposable
{
    private const string SharedPartitionKey = "all";

    private readonly PartitionedRateLimiter<HttpContext> _probeBudget;

    public DatabaseProbeRequestLimiter(IOptions<ApiRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ApiRateLimitOptions limits = options.Value;

        _probeBudget = PartitionedRateLimiter.Create<HttpContext, string>(
            _ => RateLimitPartition.GetFixedWindowLimiter(
                SharedPartitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.DatabaseProbePermitLimit,
                    Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = limits.QueueLimit
                }));
    }

    public ValueTask<RateLimitLease> AcquireAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return _probeBudget.AcquireAsync(context, 1, cancellationToken);
    }

    public void Dispose()
    {
        _probeBudget.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _probeBudget.DisposeAsync();
    }
}
