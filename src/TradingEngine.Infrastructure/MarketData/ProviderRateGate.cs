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

        await _semaphore.WaitAsync(cancellationToken);
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
