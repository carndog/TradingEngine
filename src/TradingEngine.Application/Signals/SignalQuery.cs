using NodaTime;

namespace TradingEngine.Application.Signals;

public sealed record SignalQuery(
    Instant Cutoff,
    Guid? InstrumentId = null,
    Instant? TriggeredFrom = null,
    Instant? TriggeredTo = null,
    int Page = 1,
    int PageSize = 25,
    Guid? SignalId = null,
    bool? Archived = null,
    Instant? TrackingChangedFrom = null);
