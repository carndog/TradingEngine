using System.Diagnostics.Metrics;

namespace TradingEngine.Api.RateLimiting;

internal sealed class RateLimitingTelemetry : IDisposable
{
    internal const string MeterName = "TradingEngine.Api.RateLimiting";
    internal const string CounterName = "tradingengine.api.rate_limiter.requests";
    internal const string AcceptedOutcome = "accepted";
    internal const string RejectedOutcome = "rejected";
    internal const string CancelledOutcome = "cancelled";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;

    public RateLimitingTelemetry()
    {
        _meter = new Meter(MeterName);
        _requests = _meter.CreateCounter<long>(
            CounterName,
            description: "Administration API request outcomes decided by the rate limiter, by policy and outcome.");
    }

    internal void RecordAccepted(string policy)
    {
        Record(policy, AcceptedOutcome, RateLimitPolicies.NoLimit);
    }

    internal void RecordRejected(string policy, string limit)
    {
        Record(policy, RejectedOutcome, limit);
    }

    internal void RecordCancelled(string policy)
    {
        Record(policy, CancelledOutcome, RateLimitPolicies.NoLimit);
    }

    private void Record(string policy, string outcome, string limit)
    {
        _requests.Add(
            1,
            new KeyValuePair<string, object?>("policy", policy),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("limit", limit));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
