using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Api.PriceCollection;

internal sealed class PriceCollectionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PriceCollectionHostedService> _logger;
    private readonly int _pollIntervalSeconds;

    public PriceCollectionHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<PriceCollectionHostedService> logger,
        int pollIntervalSeconds)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            CollectDuePricesHandler handler = scope.ServiceProvider
                .GetRequiredService<CollectDuePricesHandler>();
            PriceCollectionRunResult result = await handler.HandleAsync(
                new CollectDuePrices(),
                stoppingToken);
            LogSummary(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Price collection run failed unexpectedly; the next scheduled tick will retry.");
        }
    }

    private void LogSummary(PriceCollectionRunResult result)
    {
        int observed = 0;
        int marketClosed = 0;
        int duplicates = 0;
        int throttled = 0;
        int priceUnavailable = 0;
        int failed = 0;

        foreach (PriceCollectionItemResult item in result.Items)
        {
            switch (item.Outcome)
            {
                case PriceCollectionOutcome.Observed:
                    observed++;
                    break;
                case PriceCollectionOutcome.SkippedMarketClosed:
                    marketClosed++;
                    break;
                case PriceCollectionOutcome.SkippedDuplicate:
                    duplicates++;
                    break;
                case PriceCollectionOutcome.Throttled:
                    throttled++;
                    break;
                case PriceCollectionOutcome.PriceUnavailable:
                    priceUnavailable++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        _logger.LogInformation(
            "Price collection run {InvocationId} completed: {Observed} observed, " +
            "{MarketClosed} market-closed, {Duplicates} duplicate, {Throttled} throttled, " +
            "{PriceUnavailable} price-unavailable, {Failed} failed; " +
            "deadlineReached={DeadlineReached}.",
            result.InvocationId,
            observed,
            marketClosed,
            duplicates,
            throttled,
            priceUnavailable,
            failed,
            result.DeadlineReached);
    }
}
