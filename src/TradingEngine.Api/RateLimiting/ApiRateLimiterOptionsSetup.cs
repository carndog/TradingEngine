using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace TradingEngine.Api.RateLimiting;

internal sealed class ApiRateLimiterOptionsSetup : IConfigureOptions<RateLimiterOptions>
{
    private const string SharedConcurrencyPartition = "all";

    private readonly ConcurrencyLimiter _concurrency;
    private readonly IRequestBudgetLimiter _budget;
    private readonly RateLimitingTelemetry _telemetry;

    public ApiRateLimiterOptionsSetup(
        ConcurrencyLimiter concurrency,
        IRequestBudgetLimiter budget,
        RateLimitingTelemetry telemetry)
    {
        _concurrency = concurrency ?? throw new ArgumentNullException(nameof(concurrency));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.GlobalLimiter = new MeteredRequestLimiter(
            PartitionedRateLimiter.CreateChained(
                SharedConcurrencyLimiter(),
                _budget.Limiter),
            _telemetry);
        options.OnRejected = OnRejectedAsync;
    }

    private PartitionedRateLimiter<HttpContext> SharedConcurrencyLimiter()
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(
            _ => RateLimitPartition.Get<string>(
                SharedConcurrencyPartition,
                _ => _concurrency));
    }

    private async ValueTask OnRejectedAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        string policy = RateLimitPolicies.ForRequest(context.HttpContext);

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            _telemetry.RecordRejected(policy, RateLimitPolicies.RateLimit);
            context.HttpContext.Response.Headers.RetryAfter =
                ((long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            _telemetry.RecordRejected(policy, RateLimitPolicies.ConcurrencyLimit);
        }

        await ApiProblemDetails.TooManyRequests().ExecuteAsync(context.HttpContext);
    }
}
