using TradingEngine.Domain.SignalTracking;

namespace TradingEngine.Application.Signals;

public sealed record SignalEvidence(
    Guid SignalId,
    Guid AlertId,
    Guid InstrumentId,
    string Symbol,
    string Exchange,
    Guid RevisionId,
    string ZoneId,
    string Condition,
    string ActionId,
    SignalPriceBaseline Baseline,
    string EmailStatus,
    bool MonitoringPaused,
    bool InstrumentRemoved,
    bool SourceRevisionActive,
    int SamplingIntervalSeconds,
    SignalQuote? Quote);
