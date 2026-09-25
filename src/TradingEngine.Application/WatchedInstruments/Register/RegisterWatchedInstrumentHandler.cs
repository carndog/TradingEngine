using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed class RegisterWatchedInstrumentHandler
{
    private readonly IClock _clock;
    private readonly IWatchedInstrumentStore _store;

    public RegisterWatchedInstrumentHandler(
        IClock clock,
        IWatchedInstrumentStore store)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<WatchedInstrument>> HandleAsync(
        RegisterWatchedInstrument command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Definition);

        Instant occurredAt = _clock.GetCurrentInstant();
        Result<WatchedInstrumentFields> fields = WatchedInstrument.Validate(
            command.Symbol,
            command.Exchange,
            command.QuoteCurrency,
            command.SamplingIntervalSeconds);

        if (fields.IsFailure)
        {
            return fields.Error;
        }

        Result<Guid> stored = await _store.AddAsync(
            new WatchedInstrumentRegistration(
                fields.Value,
                command.MonitoringState,
                occurredAt,
                command.Definition),
            cancellationToken);

        if (stored.IsFailure)
        {
            return stored.Error;
        }

        Result<WatchedInstrument> created = WatchedInstrument.Create(
            stored.Value,
            fields.Value.Symbol,
            fields.Value.Exchange,
            fields.Value.QuoteCurrency,
            fields.Value.SamplingIntervalSeconds,
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

        return created;
    }
}
