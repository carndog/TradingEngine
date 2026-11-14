using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Settings;

public sealed class UpdateWatchedInstrumentSettingsHandler
{
    private readonly IWatchedInstrumentStore _store;
    private readonly IClock _clock;
    private readonly MonitoringReadinessEvaluator _readiness;
    private readonly ListingStatusEvaluator _listingStatus;
    private readonly CollectionStatusEvaluator _collectionStatus;

    public UpdateWatchedInstrumentSettingsHandler(
        IWatchedInstrumentStore store,
        IClock clock,
        MonitoringReadinessEvaluator readiness,
        ListingStatusEvaluator listingStatus,
        CollectionStatusEvaluator collectionStatus)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _listingStatus = listingStatus
            ?? throw new ArgumentNullException(nameof(listingStatus));
        _collectionStatus = collectionStatus
            ?? throw new ArgumentNullException(nameof(collectionStatus));
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

        Result applied = await ApplyDesiredStateAsync(
            instrument,
            command,
            changedAt,
            cancellationToken);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        PriceDisplayPreferences display = MergeDisplay(snapshot.Value.Display, command);
        if (instrument.MonitoringState == previousState
            && instrument.SamplingIntervalSeconds == previousInterval
            && display == snapshot.Value.Display)
        {
            return await DecorateAsync(snapshot.Value, cancellationToken);
        }

        Result<WatchedInstrumentSettingsSnapshot> saved =
            await _store.SaveSettingsAsync(
                instrument,
                display,
                command.ExpectedConcurrencyToken,
                cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        return await DecorateAsync(saved.Value, cancellationToken);
    }

    private async Task<Result<WatchedInstrumentSettingsSnapshot>> DecorateAsync(
        WatchedInstrumentSettingsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        Result<ListingStatusInfo> listing = await _listingStatus.EvaluateAsync(
            snapshot.Instrument.Exchange,
            snapshot.Instrument.Symbol,
            cancellationToken);
        if (listing.IsFailure)
        {
            return listing.Error;
        }

        CollectionStatusInfo collection = await _collectionStatus.EvaluateAsync(
            snapshot.Instrument,
            cancellationToken);

        return snapshot with
        {
            Listing = listing.Value,
            Collection = collection
        };
    }

    private async Task<Result> ApplyDesiredStateAsync(
        WatchedInstrument instrument,
        UpdateWatchedInstrumentSettings command,
        Instant changedAt,
        CancellationToken cancellationToken)
    {
        if (command.MonitoringState == MonitoringState.Monitored
            && instrument.MonitoringState == MonitoringState.Configured)
        {
            Result ready = await _readiness.EvaluateAsync(
                instrument.Exchange,
                instrument.Symbol,
                instrument.QuoteCurrency,
                cancellationToken);
            if (ready.IsFailure)
            {
                return ready.Error;
            }

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

    private static PriceDisplayPreferences MergeDisplay(
        PriceDisplayPreferences current,
        UpdateWatchedInstrumentSettings command)
    {
        return new PriceDisplayPreferences(
            command.PriceDisplayDecimalPlaces ?? current.DecimalPlaces,
            command.PriceDisplayUnit ?? current.Unit);
    }
}
