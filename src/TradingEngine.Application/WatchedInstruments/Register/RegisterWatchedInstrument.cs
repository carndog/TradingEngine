using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed record RegisterWatchedInstrument(
    WatchedInstrumentId Id,
    BrokerInstrumentCode BrokerCode,
    ExchangeCode Exchange,
    CurrencyCode Currency,
    SamplingPolicy SamplingPolicy);
