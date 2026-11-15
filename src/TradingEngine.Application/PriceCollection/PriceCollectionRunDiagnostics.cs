using TradingEngine.Application.MarketData;

namespace TradingEngine.Application.PriceCollection;

public sealed record PriceCollectionRunDiagnostics
{
    public int Monitored { get; init; }

    public int Due { get; init; }

    public int Eligible { get; init; }

    public int Requested { get; init; }

    public int Returned { get; init; }

    public int Persisted { get; init; }

    public int Deferred { get; init; }

    public int Failed { get; init; }

    public int Unprocessed { get; init; }

    public int ExpiredPendingSlots { get; init; }

    public bool EligibilityScanCompleted { get; init; }

    public MarketDataRequestStatistics Requests { get; init; } = MarketDataRequestStatistics.Empty;
}
