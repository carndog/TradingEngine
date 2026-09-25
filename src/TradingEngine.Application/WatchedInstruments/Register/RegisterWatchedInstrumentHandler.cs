using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed class RegisterWatchedInstrumentHandler
{
    private readonly IClock _clock;
    private readonly IWatchedInstrumentStore _store;
    private readonly IWatchedInstrumentIdGenerator _idGenerator;

    public RegisterWatchedInstrumentHandler(
        IClock clock,
        IWatchedInstrumentStore store,
        IWatchedInstrumentIdGenerator idGenerator)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    }

    public async Task<Result<WatchedInstrument>> HandleAsync(
        RegisterWatchedInstrument command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Definition);

        Instant occurredAt = _clock.GetCurrentInstant();
        Result<WatchedInstrument> created = WatchedInstrument.Create(
            _idGenerator.NewId(),
            command.Symbol,
            command.Exchange,
            command.QuoteCurrency,
            command.SamplingIntervalSeconds,
            occurredAt);

        if (created.IsFailure)
        {
            return created;
        }

        if (command.MonitoringState == MonitoringState.Monitored)
        {
            Result monitoring = created.Value.StartMonitoring(
                command.SamplingIntervalSeconds,
                occurredAt);

            if (monitoring.IsFailure)
            {
                return monitoring.Error;
            }
        }

        Result stored = await _store.AddAsync(
            new WatchedInstrumentConfiguration(created.Value, command.Definition),
            cancellationToken);

        if (stored.IsFailure)
        {
            return stored.Error;
        }

        return created;
    }
}
