using System.Diagnostics;
using TradingEngine.Api.Recommendations;
using TradingEngine.Application.Notifications;

namespace TradingEngine.Api.Notifications;

internal sealed class NotificationDispatchHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationDispatchHostedService> _logger;
    private readonly RecommendationTelemetry _telemetry;
    private readonly int _pollIntervalSeconds;

    public NotificationDispatchHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<NotificationDispatchHostedService> logger,
        RecommendationTelemetry telemetry,
        int pollIntervalSeconds)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _pollIntervalSeconds = pollIntervalSeconds;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(
            TimeSpan.FromSeconds(_pollIntervalSeconds),
            _timeProvider);

        while (await WaitForNextTickAsync(timer, stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task<bool> WaitForNextTickAsync(
        PeriodicTimer timer,
        CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            DispatchPendingNotificationsHandler handler = scope.ServiceProvider
                .GetRequiredService<DispatchPendingNotificationsHandler>();

            using Activity? activity = RecommendationTelemetry.ActivitySource
                .StartActivity("notifications.dispatch.run");
            DispatchRunResult result = await handler.HandleAsync(
                new DispatchPendingNotifications(),
                stoppingToken);
            activity?.SetTag("invocation_id", result.InvocationId);
            activity?.SetTag("intents_processed", result.Items.Count);

            RecordMetrics(result);
            LogSummary(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _telemetry.RecordNotificationFailure();
            _logger.LogError(
                exception,
                "Notification dispatch run failed unexpectedly; " +
                "the next scheduled tick will retry.");
        }
    }

    private void RecordMetrics(DispatchRunResult result)
    {
        foreach (IntentDispatchResult item in result.Items)
        {
            string outcome = item.Outcome.ToString().ToLowerInvariant();
            _telemetry.RecordDispatchOutcome(outcome);
            if (RecommendationTelemetry.IsFailureOutcome(item.Outcome))
            {
                _telemetry.RecordNotificationFailure();
            }

            _logger.LogInformation(
                "Notification intent {IntentId} for instrument {InstrumentId} " +
                "at slot {ScheduledFor} under revision {MonitoringRuleRevisionId} " +
                "from collection {CollectionInvocationId} dispatched: {Outcome}.",
                item.IntentId,
                item.InstrumentId,
                item.ScheduledFor,
                item.MonitoringRuleRevisionId,
                item.CollectionInvocationId,
                outcome);
        }
    }

    private void LogSummary(DispatchRunResult result)
    {
        int sent = 0;
        int retryPending = 0;
        int failed = 0;
        int ambiguous = 0;
        int inFlight = 0;
        int other = 0;

        foreach (IntentDispatchResult item in result.Items)
        {
            switch (item.Outcome)
            {
                case DispatchItemOutcome.Sent:
                    sent++;
                    break;
                case DispatchItemOutcome.RetryPending:
                    retryPending++;
                    break;
                case DispatchItemOutcome.Failed:
                case DispatchItemOutcome.Suppressed:
                    failed++;
                    break;
                case DispatchItemOutcome.Ambiguous:
                    ambiguous++;
                    break;
                case DispatchItemOutcome.InFlight:
                    inFlight++;
                    break;
                default:
                    other++;
                    break;
            }
        }

        _logger.LogInformation(
            "Notification dispatch run {InvocationId} completed: {Sent} sent, " +
            "{RetryPending} retry-pending, {Failed} failed, {Ambiguous} ambiguous, " +
            "{InFlight} in-flight, {Other} skipped.",
            result.InvocationId,
            sent,
            retryPending,
            failed,
            ambiguous,
            inFlight,
            other);
    }
}
