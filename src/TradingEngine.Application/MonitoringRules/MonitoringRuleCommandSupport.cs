using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MonitoringRules;

internal static class MonitoringRuleCommandSupport
{
    internal static async Task<Result<MonitoringRuleSnapshot>> MutateAsync(
        IMonitoringRuleStore store,
        Guid instrumentId,
        byte[]? expectedConcurrencyToken,
        Instant now,
        bool nowIsPersisted,
        Func<MonitoringRule, Instant, Result> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mutate);

        if (expectedConcurrencyToken is null)
        {
            return MonitoringRuleErrors.ConcurrencyTokenRequired;
        }

        if (nowIsPersisted)
        {
            Result<Instant> persistedNow = RequirePersistable(now);
            if (persistedNow.IsFailure)
            {
                return persistedNow.Error;
            }

            now = persistedNow.Value;
        }

        Result<MonitoringRuleSnapshot> snapshot = await store.GetAsync(
            instrumentId,
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return snapshot.Error;
        }

        Result mutated = mutate(snapshot.Value.Rule, now);
        if (mutated.IsFailure)
        {
            return mutated.Error;
        }

        Result saved = await store.SaveAsync(
            new MonitoringRuleSnapshot(snapshot.Value.Rule, expectedConcurrencyToken),
            cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        return await store.GetAsync(instrumentId, cancellationToken);
    }

    internal static Result<Instant> RequirePersistable(Instant instant)
    {
        try
        {
            return Result<Instant>.Success(
                PersistedInstant.Require(instant, "instant"));
        }
        catch (ArgumentOutOfRangeException)
        {
            return MonitoringRuleErrors.InstantNotPersistable;
        }
    }

    internal static (Instant? Persisted, Error? Error) RequirePersistable(Instant? instant)
    {
        if (instant is null)
        {
            return (null, null);
        }

        Result<Instant> persisted = RequirePersistable(instant.Value);
        return persisted.IsFailure
            ? (null, persisted.Error)
            : (persisted.Value, null);
    }
}
