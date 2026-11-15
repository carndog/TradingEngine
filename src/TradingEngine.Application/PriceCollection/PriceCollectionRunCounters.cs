namespace TradingEngine.Application.PriceCollection;

internal sealed class PriceCollectionRunCounters
{
    public HashSet<Guid> Eligible { get; } = [];

    public HashSet<Guid> Requested { get; } = [];

    public HashSet<Guid> Returned { get; } = [];
}
