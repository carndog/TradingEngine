using NodaTime;

namespace TradingEngine.Infrastructure.MarketData;

public sealed class ProviderRateGate
{
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<string, Queue<Instant>> _requests =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Instant> _cooldowns =
        new(StringComparer.Ordinal);
    private readonly object _cooldownLock = new();

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
                Instant? cooldownUntil = CooldownUntil(policy.Name);
                if (cooldownUntil is not null && cooldownUntil > now)
                {
                    if (cooldownUntil >= deadline)
                    {
                        return false;
                    }

                    await Task.Delay(
                        (cooldownUntil.Value - now).ToTimeSpan(),
                        _timeProvider,
                        cancellationToken);
                    continue;
                }

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

    public void RegisterCooldown(string policyName, Instant cooldownUntil)
    {
        lock (_cooldownLock)
        {
            if (_cooldowns.TryGetValue(policyName, out Instant existing) is false
                || cooldownUntil > existing)
            {
                _cooldowns[policyName] = cooldownUntil;
            }
        }
    }

    private Instant? CooldownUntil(string policyName)
    {
        lock (_cooldownLock)
        {
            return _cooldowns.TryGetValue(policyName, out Instant until)
                ? until
                : null;
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
