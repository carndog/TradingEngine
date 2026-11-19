using TradingEngine.Application.Signals;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed record InstrumentSignalQuote(Guid InstrumentId, SignalQuote Quote);
