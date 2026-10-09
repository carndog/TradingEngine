using System.Globalization;
using System.Threading.RateLimiting;

namespace TradingEngine.Api.RateLimiting;

internal sealed class RateLimitingGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrencyLimiter _concurrency;
    private readonly IAdministrationRequestLimiter _administration;
    private readonly IDatabaseProbeRequestLimiter _databaseProbe;
    private readonly RateLimitingTelemetry _telemetry;
    private readonly RateLimitingGate _gate;

    public RateLimitingGateMiddleware(
        RequestDelegate next,
        ConcurrencyLimiter concurrency,
        IAdministrationRequestLimiter administration,
        IDatabaseProbeRequestLimiter databaseProbe,
        RateLimitingTelemetry telemetry,
        RateLimitingGate gate)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _concurrency = concurrency ?? throw new ArgumentNullException(nameof(concurrency));
        _administration = administration ?? throw new ArgumentNullException(nameof(administration));
        _databaseProbe = databaseProbe ?? throw new ArgumentNullException(nameof(databaseProbe));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _gate = gate;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string policy = _gate == RateLimitingGate.Administration
            ? RateLimitPolicies.ForAdminRequest(context.Request)
            : RateLimitPolicies.DatabaseProbe;

        RateLimitLease? concurrencyLease = null;
        RateLimitLease? budgetLease = null;
        try
        {
            try
            {
                concurrencyLease = await _concurrency.AcquireAsync(1, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                _telemetry.RecordCancelled(policy);
                throw;
            }

            if (concurrencyLease.IsAcquired is false)
            {
                _telemetry.RecordRejected(policy, RateLimitPolicies.ConcurrencyLimit);
                await WriteRejectedAsync(context, concurrencyLease);
                return;
            }

            try
            {
                budgetLease = await BudgetLimiter().AcquireAsync(context, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                _telemetry.RecordCancelled(policy);
                throw;
            }

            if (budgetLease.IsAcquired is false)
            {
                _telemetry.RecordRejected(policy, RateLimitPolicies.RateLimit);
                await WriteRejectedAsync(context, budgetLease);
                return;
            }

            _telemetry.RecordAccepted(policy);
            await _next(context);
        }
        finally
        {
            budgetLease?.Dispose();
            concurrencyLease?.Dispose();
        }
    }

    private IRequestLimiter BudgetLimiter()
    {
        return _gate == RateLimitingGate.Administration ? _administration : _databaseProbe;
    }

    private static async Task WriteRejectedAsync(HttpContext context, RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.Response.Headers.RetryAfter =
                ((long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await ApiProblemDetails.TooManyRequests().ExecuteAsync(context);
    }
}
