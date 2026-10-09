using System.Diagnostics.Metrics;

namespace TradingEngine.Api.RateLimiting;

internal sealed class RateLimitingTelemetry : IDisposable
{
    internal const string MeterName = "TradingEngine.Api.RateLimiting";
    internal const string CounterName = "tradingengine.api.rate_limiter.requests";
    internal const string AcceptedOutcome = "accepted";
    internal const string RejectedOutcome = "rejected";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;

    public RateLimitingTelemetry()
    {
        _meter = new Meter(MeterName);
        _requests = _meter.CreateCounter<long>(
            CounterName,
            description: "Administration API requests evaluated by the rate limiter, by policy and outcome.");
    }

    internal void Record(string policy, bool acquired)
    {
        _requests.Add(
            1,
            new KeyValuePair<string, object?>("policy", policy),
            new KeyValuePair<string, object?>("outcome", acquired ? AcceptedOutcome : RejectedOutcome));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
