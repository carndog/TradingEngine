using TradingEngine.Application.WeeklyReports;

namespace TradingEngine.Api.WeeklyReports;

internal sealed class WeeklyReportHostedService(IServiceScopeFactory scopes, TimeProvider time,
    WeeklyReportOptions options, WeeklyReportTelemetry telemetry, ILogger<WeeklyReportHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(options.PollIntervalSeconds), time);
        try
        {
            do
            {
                await RunAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            WeeklyReportRunResult result = await scope.ServiceProvider.GetRequiredService<RunWeeklyReportsHandler>().HandleAsync(cancellationToken);
            telemetry.Record(result);
            if (result.Generated || result.GenerationFailed || result.Deliveries.Count > 0)
            {
                logger.LogInformation("Weekly reports: generated={Generated}, generationFailed={GenerationFailed}, deliveries={Deliveries}.",
                    result.Generated, result.GenerationFailed, result.Deliveries.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            telemetry.RecordFailure();
            logger.LogError("Weekly report tick failed; durable claims and pending deliveries will be retried.");
        }
    }
}
