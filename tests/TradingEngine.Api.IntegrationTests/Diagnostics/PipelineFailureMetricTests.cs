using System.Diagnostics.Metrics;
using TradingEngine.Api.PriceCollection;
using TradingEngine.Api.Recommendations;
using TradingEngine.Application.Notifications;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class PipelineFailureMetricTests
{
    [Test]
    public void RecordNotificationFailure_WhenThreeFailuresInOneWindow_MeetsAlertThreshold()
    {
        long summed = 0;
        using MeterListener listener = Listen(
            RecommendationTelemetry.MeterName,
            RecommendationTelemetry.NotificationFailuresCounterName,
            value => summed += value);
        using RecommendationTelemetry telemetry = new();

        telemetry.RecordNotificationFailure();
        telemetry.RecordNotificationFailure();
        telemetry.RecordNotificationFailure();
        listener.RecordObservableInstruments();

        Assert.That(summed, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void RecordFailures_WhenThreeCollectionFailuresInOneWindow_MeetsAlertThreshold()
    {
        long summed = 0;
        using MeterListener listener = Listen(
            PriceCollectionTelemetry.MeterName,
            PriceCollectionTelemetry.FailuresCounterName,
            value => summed += value);
        using PriceCollectionTelemetry telemetry = new();

        telemetry.RecordFailures(3);
        listener.RecordObservableInstruments();

        Assert.That(summed, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void IsFailureOutcome_WhenInFlight_ReturnsFalse()
    {
        Assert.That(
            RecommendationTelemetry.IsFailureOutcome(DispatchItemOutcome.InFlight),
            Is.False);
    }

    [Test]
    public void IsFailureOutcome_WhenGenuineFailures_ReturnsTrue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                RecommendationTelemetry.IsFailureOutcome(
                    DispatchItemOutcome.Ambiguous),
                Is.True);
            Assert.That(
                RecommendationTelemetry.IsFailureOutcome(
                    DispatchItemOutcome.Failed),
                Is.True);
            Assert.That(
                RecommendationTelemetry.IsFailureOutcome(
                    DispatchItemOutcome.Suppressed),
                Is.True);
        });
    }

    private static MeterListener Listen(
        string meterName,
        string instrumentName,
        Action<long> onMeasurement)
    {
        MeterListener listener = new()
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == meterName
                    && instrument.Name == instrumentName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
                onMeasurement(measurement));
        listener.Start();
        return listener;
    }
}
