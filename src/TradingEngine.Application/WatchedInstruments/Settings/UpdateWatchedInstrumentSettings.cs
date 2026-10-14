using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments.Settings;

public sealed record UpdateWatchedInstrumentSettings(
    Guid InstrumentId,
    MonitoringState MonitoringState,
    int SamplingIntervalSeconds,
    byte[]? ExpectedConcurrencyToken);
