using System.Diagnostics;
using TradingEngine.Application.Recommendations;

namespace TradingEngine.Api.Recommendations;

internal sealed class RecommendationEvaluationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecommendationEvaluationHostedService> _logger;
    private readonly RecommendationTelemetry _telemetry;
    private readonly int _pollIntervalSeconds;

    public RecommendationEvaluationHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RecommendationEvaluationHostedService> logger,
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
            EvaluateSavedPricesHandler handler = scope.ServiceProvider
                .GetRequiredService<EvaluateSavedPricesHandler>();

            using Activity? activity = RecommendationTelemetry.ActivitySource
                .StartActivity("recommendations.evaluate.run");
            EvaluationRunResult result = await handler.HandleAsync(
                new EvaluateSavedPrices(),
                stoppingToken);
            activity?.SetTag("invocation_id", result.InvocationId);
            activity?.SetTag("instruments_processed", result.Items.Count);

            RecordMetrics(result);
            LogSummary(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Saved-price evaluation run failed unexpectedly; " +
                "the next scheduled tick will retry.");
        }
    }

    private void RecordMetrics(EvaluationRunResult result)
    {
        foreach (InstrumentEvaluationResult item in result.Items)
        {
            int triggered = item.Evaluated - item.NoAction;
            _telemetry.RecordEvaluations(triggered, "triggered");
            _telemetry.RecordEvaluations(item.NoAction, "no_action");
            _telemetry.RecordEvaluations(item.SkippedNoRevision, "no_revision");
            _telemetry.RecordEvaluations(
                item.FailureCode is null ? 0 : 1,
                "failed");
            _telemetry.RecordIntentsCreated(item.IntentsCreated);
        }
    }

    private void LogSummary(EvaluationRunResult result)
    {
        int evaluated = 0;
        int noAction = 0;
        int skipped = 0;
        int intents = 0;
        int failures = 0;

        foreach (InstrumentEvaluationResult item in result.Items)
        {
            evaluated += item.Evaluated;
            noAction += item.NoAction;
            skipped += item.SkippedNoRevision;
            intents += item.IntentsCreated;
            if (item.FailureCode is not null)
            {
                failures++;
                _logger.LogWarning(
                    "Saved-price evaluation for instrument {InstrumentId} stopped at " +
                    "slot {FailedSlot}: {FailureCode}.",
                    item.InstrumentId,
                    item.FailedSlot,
                    item.FailureCode);
            }
        }

        _logger.LogInformation(
            "Evaluation run {InvocationId} completed: {Evaluated} evaluated, " +
            "{NoAction} no-action, {Skipped} no-revision, {Intents} intents, " +
            "{Failures} instrument failures; deadlineReached={DeadlineReached}.",
            result.InvocationId,
            evaluated,
            noAction,
            skipped,
            intents,
            failures,
            result.DeadlineReached);
    }
}
