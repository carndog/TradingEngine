using System.Diagnostics.Metrics;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.WeeklyReports;

namespace TradingEngine.Api.WeeklyReports;

internal sealed class WeeklyReportTelemetry : IDisposable
{
    internal const string MeterName = "TradingEngine.Api.WeeklyReports";
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _generated;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _deliveries;

    public WeeklyReportTelemetry()
    {
        _generated = _meter.CreateCounter<long>("tradingengine.weekly_reports.generated");
        _failures = _meter.CreateCounter<long>("tradingengine.weekly_reports.failures");
        _deliveries = _meter.CreateCounter<long>("tradingengine.weekly_reports.deliveries");
    }

    public void Record(WeeklyReportRunResult result)
    {
        if (result.Generated)
        {
            _generated.Add(1);
        }

        if (result.GenerationFailed)
        {
            RecordFailure();
        }

        foreach (EmailDispatchResult delivery in result.Deliveries)
        {
            _deliveries.Add(1, new KeyValuePair<string, object?>("outcome", delivery.Outcome.ToString()));
            if (delivery.Outcome is DispatchItemOutcome.Failed or DispatchItemOutcome.Suppressed or DispatchItemOutcome.Ambiguous)
            {
                RecordFailure();
            }
        }
    }

    public void RecordFailure()
    {
        _failures.Add(1);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
