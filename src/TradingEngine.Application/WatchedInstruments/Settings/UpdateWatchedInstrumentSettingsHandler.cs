using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Settings;

public sealed class UpdateWatchedInstrumentSettingsHandler
{
    private readonly IWatchedInstrumentStore _store;
    private readonly IClock _clock;

    public UpdateWatchedInstrumentSettingsHandler(
        IWatchedInstrumentStore store,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<WatchedInstrumentSettingsSnapshot>> HandleAsync(
        UpdateWatchedInstrumentSettings command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ExpectedConcurrencyToken is null)
        {
            return WatchedInstrumentErrors.ConcurrencyTokenRequired;
        }

        if (Enum.IsDefined(command.MonitoringState) is false)
        {
            return WatchedInstrumentErrors.MonitoringStateUndefined;
        }

        if (command.PriceDisplayDecimalPlaces is < PriceDisplayPreferences.MinimumDecimalPlaces
            or > PriceDisplayPreferences.MaximumDecimalPlaces)
        {
            return PriceDisplayErrors.DecimalPlacesOutOfRange;
        }

        if (command.PriceDisplayUnit is not null && Enum.IsDefined(command.PriceDisplayUnit.Value) is false)
        {
            return PriceDisplayErrors.UnitUndefined;
        }

        Result<WatchedInstrumentSettingsSnapshot> snapshot = await _store.GetSettingsAsync(
            command.InstrumentId,
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return snapshot.Error;
        }

        if (snapshot.Value.ConcurrencyToken.SequenceEqual(command.ExpectedConcurrencyToken) is false)
        {
            return WatchedInstrumentErrors.ConcurrentChange;
        }

        WatchedInstrument instrument = snapshot.Value.Instrument;
        MonitoringState previousState = instrument.MonitoringState;
        int previousInterval = instrument.SamplingIntervalSeconds;
        Instant changedAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));

        Result applied = ApplyDesiredState(instrument, command, changedAt);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        PriceDisplayPreferences display = MergeDisplay(snapshot.Value.Display, command);
        if (instrument.MonitoringState == previousState
            && instrument.SamplingIntervalSeconds == previousInterval
            && display == snapshot.Value.Display)
        {
            return snapshot;
        }

        return await _store.SaveSettingsAsync(
            instrument,
            display,
            command.ExpectedConcurrencyToken,
            cancellationToken);
    }

    private static PriceDisplayPreferences MergeDisplay(
        PriceDisplayPreferences current,
        UpdateWatchedInstrumentSettings command)
    {
        return new PriceDisplayPreferences(
            command.PriceDisplayDecimalPlaces ?? current.DecimalPlaces,
            command.PriceDisplayUnit ?? current.Unit);
    }

    private static Result ApplyDesiredState(
        WatchedInstrument instrument,
        UpdateWatchedInstrumentSettings command,
        Instant changedAt)
    {
        if (command.MonitoringState == MonitoringState.Monitored
            && instrument.MonitoringState == MonitoringState.Configured)
        {
            return instrument.StartMonitoring(
                command.SamplingIntervalSeconds,
                changedAt);
        }

        if (command.MonitoringState == MonitoringState.Configured
            && instrument.MonitoringState == MonitoringState.Monitored)
        {
            Result stopped = instrument.StopMonitoring(changedAt);
            if (stopped.IsFailure)
            {
                return stopped;
            }
        }

        if (instrument.SamplingIntervalSeconds != command.SamplingIntervalSeconds)
        {
            return instrument.ChangeSamplingInterval(
                command.SamplingIntervalSeconds,
                changedAt);
        }

        return Result.Success();
    }
}
