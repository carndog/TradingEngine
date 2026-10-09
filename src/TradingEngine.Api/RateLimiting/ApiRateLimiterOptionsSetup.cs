using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TradingEngine.Api.Authentication;

namespace TradingEngine.Api.RateLimiting;

internal sealed class ApiRateLimiterOptionsSetup : IConfigureOptions<RateLimiterOptions>
{
    internal const string DatabaseProbePolicyName = "database-probe";
    internal const string ReadPolicy = "admin-read";
    internal const string WritePolicy = "admin-write";
    internal const string ConcurrencyPolicy = "api-concurrency";

    private const string UnverifiedCallerPartition = "unverified";
    private const string SharedPartition = "all";

    private readonly ApiRateLimitOptions _limits;
    private readonly RateLimitingTelemetry _telemetry;

    public ApiRateLimiterOptionsSetup(
        IOptions<ApiRateLimitOptions> limits,
        RateLimitingTelemetry telemetry)
    {
        _limits = limits?.Value ?? throw new ArgumentNullException(nameof(limits));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = OnRejectedAsync;
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            CreateCallerBudgetLimiter(),
            CreateSharedConcurrencyLimiter());
        options.AddPolicy<string>(
            DatabaseProbePolicyName,
            _ => RateLimitPartition.Get<string>(
                SharedPartition,
                _ => CreateWindowLimiter(_limits.DatabaseProbePermitLimit, DatabaseProbePolicyName)));
    }

    private PartitionedRateLimiter<HttpContext> CreateCallerBudgetLimiter()
    {
        return PartitionedRateLimiter.Create<HttpContext, (string Caller, bool Write)>(context =>
        {
            (string Caller, bool Write) partition =
                (ResolveCaller(context), IsWrite(context.Request.Method));

            return RateLimitPartition.Get<(string Caller, bool Write)>(
                partition,
                key => CreateWindowLimiter(
                    key.Write ? _limits.WritePermitLimit : _limits.ReadPermitLimit,
                    key.Write ? WritePolicy : ReadPolicy));
        });
    }

    private PartitionedRateLimiter<HttpContext> CreateSharedConcurrencyLimiter()
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(
            _ => RateLimitPartition.Get<string>(
                SharedPartition,
                _ => new MeteredRateLimiter(
                    new ConcurrencyLimiter(
                        new ConcurrencyLimiterOptions
                        {
                            PermitLimit = _limits.ConcurrencyPermitLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = _limits.QueueLimit
                        }),
                    _telemetry,
                    ConcurrencyPolicy)));
    }

    private MeteredRateLimiter CreateWindowLimiter(int permitLimit, string policy)
    {
        return new MeteredRateLimiter(
            new FixedWindowRateLimiter(
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(_limits.WindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = _limits.QueueLimit
                }),
            _telemetry,
            policy);
    }

    private static string ResolveCaller(HttpContext context)
    {
        string? identifier = context.User is null
            ? null
            : EasyAuthClientPrincipal.StableIdentifier(context.User);

        return identifier ?? UnverifiedCallerPartition;
    }

    private static bool IsWrite(string method)
    {
        return HttpMethods.IsGet(method) is false && HttpMethods.IsHead(method) is false;
    }

    private static async ValueTask OnRejectedAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await ApiProblemDetails.TooManyRequests().ExecuteAsync(context.HttpContext);
    }
}
