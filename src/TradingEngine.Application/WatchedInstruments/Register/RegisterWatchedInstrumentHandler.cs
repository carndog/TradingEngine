using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed class RegisterWatchedInstrumentHandler
{
    private readonly IClock _clock;
    private readonly IWatchedInstrumentStore _store;

    public RegisterWatchedInstrumentHandler(IClock clock, IWatchedInstrumentStore store)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<WatchedInstrument> HandleAsync(
        RegisterWatchedInstrument command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Instant occurredAt = _clock.GetCurrentInstant();
        WatchedInstrument instrument = WatchedInstrument.Create(
            command.Id,
            command.Symbol,
            command.Exchange,
            command.QuoteCurrency,
            command.SamplingPolicy,
            occurredAt);

        await _store.AddAsync(instrument, cancellationToken);

        return instrument;
    }
}
