using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TradingEngine.Api.Recommendations;

internal sealed class RecommendationTelemetry : IDisposable
{
    internal const string MeterName = "TradingEngine.Api.Recommendations";
    internal const string ActivitySourceName = "TradingEngine.Api.Recommendations";
    internal const string EvaluationsCounterName = "tradingengine.recommendations.evaluations";
    internal const string IntentsCounterName = "tradingengine.recommendations.intents_created";
    internal const string DispatchAttemptsCounterName = "tradingengine.notifications.dispatch_attempts";
    internal const string NotificationFailuresCounterName = "tradingengine.notifications.failures";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly Meter _meter;
    private readonly Counter<long> _evaluations;
    private readonly Counter<long> _intentsCreated;
    private readonly Counter<long> _dispatchAttempts;
    private readonly Counter<long> _notificationFailures;

    public RecommendationTelemetry()
    {
        _meter = new Meter(MeterName);
        _evaluations = _meter.CreateCounter<long>(
            EvaluationsCounterName,
            description: "Saved-price evaluation outcomes by result kind.");
        _intentsCreated = _meter.CreateCounter<long>(
            IntentsCounterName,
            description: "Durable notification intents committed by the evaluator.");
        _dispatchAttempts = _meter.CreateCounter<long>(
            DispatchAttemptsCounterName,
            description: "Notification dispatch outcomes by result kind.");
        _notificationFailures = _meter.CreateCounter<long>(
            NotificationFailuresCounterName,
            description: "Notification dispatches that failed, stayed ambiguous or were suppressed.");
    }

    internal void RecordEvaluations(int count, string outcome)
    {
        if (count <= 0)
        {
            return;
        }

        _evaluations.Add(
            count,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    internal void RecordIntentsCreated(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _intentsCreated.Add(count);
    }

    internal void RecordDispatchOutcome(string outcome)
    {
        _dispatchAttempts.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    internal void RecordNotificationFailure()
    {
        _notificationFailures.Add(1);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
