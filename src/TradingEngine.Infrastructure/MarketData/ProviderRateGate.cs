using NodaTime;

namespace TradingEngine.Infrastructure.MarketData;

public sealed class ProviderRateGate
{
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<string, Queue<Instant>> _requests =
        new(StringComparer.Ordinal);

    public ProviderRateGate(IClock clock, TimeProvider timeProvider)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<bool> WaitForSlotAsync(
        ProviderRateLimitPolicy policy,
        Instant deadline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken) is false)
        {
            TimeSpan budget = RemainingFor(deadline);
            if (budget <= TimeSpan.Zero)
            {
                return false;
            }

            using CancellationTokenSource budgetScope = new(budget, _timeProvider);
            using CancellationTokenSource linked = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken, budgetScope.Token);
            try
            {
                await _semaphore.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested is false)
            {
                return false;
            }
        }

        try
        {
            Queue<Instant> requests = RequestsFor(policy.Name);
            while (true)
            {
                Instant now = _clock.GetCurrentInstant();
                while (requests.Count > 0
                    && requests.Peek().Plus(Duration.FromTimeSpan(policy.Window)) <= now)
                {
                    requests.Dequeue();
                }

                if (now >= deadline)
                {
                    return false;
                }

                if (requests.Count < policy.Limit)
                {
                    requests.Enqueue(now);
                    return true;
                }

                Instant permittedAt = requests.Peek().Plus(Duration.FromTimeSpan(policy.Window));
                if (permittedAt >= deadline)
                {
                    return false;
                }

                TimeSpan delay = (permittedAt - now).ToTimeSpan();
                await Task.Delay(delay, _timeProvider, cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private TimeSpan RemainingFor(Instant deadline)
    {
        TimeSpan remaining = (deadline - _clock.GetCurrentInstant()).ToTimeSpan();
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private Queue<Instant> RequestsFor(string policyName)
    {
        if (_requests.TryGetValue(policyName, out Queue<Instant>? requests) is false)
        {
            requests = new Queue<Instant>();
            _requests[policyName] = requests;
        }

        return requests;
    }
}
