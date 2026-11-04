using System.Collections.Concurrent;
using NodaTime;

namespace TradingEngine.Infrastructure.MarketData;

public sealed class EToroInstrumentIdCache
{
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, (long InstrumentId, Instant ExpiresAt)> _entries =
        new(StringComparer.Ordinal);

    public EToroInstrumentIdCache(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool TryGet(
        string exchange,
        string symbol,
        string quoteCurrency,
        out long instrumentId)
    {
        string key = KeyFor(exchange, symbol, quoteCurrency);
        if (_entries.TryGetValue(key, out (long InstrumentId, Instant ExpiresAt) entry)
            && entry.ExpiresAt > _clock.GetCurrentInstant())
        {
            instrumentId = entry.InstrumentId;
            return true;
        }

        instrumentId = default;
        return false;
    }

    public void Store(
        string exchange,
        string symbol,
        string quoteCurrency,
        long instrumentId,
        TimeSpan lifetime)
    {
        Instant expiresAt = _clock.GetCurrentInstant().Plus(Duration.FromTimeSpan(lifetime));
        _entries[KeyFor(exchange, symbol, quoteCurrency)] = (instrumentId, expiresAt);
    }

    private static string KeyFor(string exchange, string symbol, string quoteCurrency)
    {
        return string.Join(
            '|',
            exchange.ToUpperInvariant(),
            symbol.ToUpperInvariant(),
            quoteCurrency.ToUpperInvariant());
    }
}
