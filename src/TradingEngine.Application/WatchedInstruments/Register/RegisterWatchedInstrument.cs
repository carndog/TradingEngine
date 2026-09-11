using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed record RegisterWatchedInstrument(
    WatchedInstrumentId Id,
    InstrumentSymbol Symbol,
    ExchangeCode Exchange,
    QuoteCurrencyCode QuoteCurrency,
    SamplingPolicy SamplingPolicy);
