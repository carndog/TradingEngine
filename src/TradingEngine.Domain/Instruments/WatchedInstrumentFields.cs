namespace TradingEngine.Domain.Instruments;

public sealed record WatchedInstrumentFields
{
    internal WatchedInstrumentFields(
        string symbol,
        string exchange,
        string quoteCurrency,
        int samplingIntervalSeconds)
    {
        Symbol = symbol;
        Exchange = exchange;
        QuoteCurrency = quoteCurrency;
        SamplingIntervalSeconds = samplingIntervalSeconds;
    }

    public string Symbol { get; }

    public string Exchange { get; }

    public string QuoteCurrency { get; }

    public int SamplingIntervalSeconds { get; }
}
