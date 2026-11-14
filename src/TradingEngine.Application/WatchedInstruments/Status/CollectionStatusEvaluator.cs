using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Status;

public sealed class CollectionStatusEvaluator
{
    private readonly INativeQuoteUnitMap _quoteUnits;
    private readonly IMarketSessionPolicy _sessionPolicy;
    private readonly IPriceObservationStore _observations;
    private readonly IPriceObservationHistoryReader _history;
    private readonly PriceCollectionOptions _options;
    private readonly IClock _clock;

    public CollectionStatusEvaluator(
        INativeQuoteUnitMap quoteUnits,
        IMarketSessionPolicy sessionPolicy,
        IPriceObservationStore observations,
        IPriceObservationHistoryReader history,
        PriceCollectionOptions options,
        IClock clock)
    {
        _quoteUnits = quoteUnits ?? throw new ArgumentNullException(nameof(quoteUnits));
        _sessionPolicy = sessionPolicy
            ?? throw new ArgumentNullException(nameof(sessionPolicy));
        _observations = observations
            ?? throw new ArgumentNullException(nameof(observations));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<CollectionStatusInfo> EvaluateAsync(
        WatchedInstrument instrument,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        if (instrument.MonitoringState != MonitoringState.Monitored)
        {
            return new CollectionStatusInfo(
                InstrumentCollectionStatus.NotMonitored,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        if (_options.Enabled is false)
        {
            return new CollectionStatusInfo(
                InstrumentCollectionStatus.CollectorDisabled,
                "price_collection.disabled",
                "The price collector is disabled for this deployment.",
                null,
                null,
                null,
                null);
        }

        Instant now = _clock.GetCurrentInstant();
        ExchangeSessionStatus session = _sessionPolicy.GetStatus(
            instrument.Exchange,
            now);
        if (session == ExchangeSessionStatus.Unconfigured)
        {
            return new CollectionStatusInfo(
                InstrumentCollectionStatus.UnsupportedVenue,
                PriceCollectionErrors.ExchangeSessionUnconfigured.Code,
                PriceCollectionErrors.ExchangeSessionUnconfigured.Description,
                null,
                null,
                null,
                null);
        }

        Result<NativeQuoteUnitResolution> resolved = await _quoteUnits.ResolveAsync(
            instrument.Exchange,
            instrument.Symbol,
            cancellationToken);
        if (resolved.IsFailure)
        {
            return new CollectionStatusInfo(
                InstrumentCollectionStatus.NativeUnitUnconfirmed,
                resolved.Error.Code,
                resolved.Error.Description,
                null,
                null,
                null,
                null);
        }

        NativeQuoteUnitResolution resolution = resolved.Value;
        if (resolution.SourceConflict)
        {
            return new CollectionStatusInfo(
                InstrumentCollectionStatus.NativeUnitConflict,
                MarketDataErrors.QuoteCurrencySourceConflict.Code,
                MarketDataErrors.QuoteCurrencySourceConflict.Description,
                null,
                null,
                null,
                null);
        }

        if (resolution.NativeQuoteCurrency is null)
        {
            return new CollectionStatusInfo(
                InstrumentCollectionStatus.NativeUnitUnconfirmed,
                MarketDataErrors.QuoteCurrencyUnconfigured.Code,
                MarketDataErrors.QuoteCurrencyUnconfigured.Description,
                null,
                null,
                null,
                null);
        }

        if (string.Equals(
                resolution.NativeQuoteCurrency,
                instrument.QuoteCurrency,
                StringComparison.Ordinal) is false)
        {
            return new CollectionStatusInfo(
                InstrumentCollectionStatus.NativeUnitConflict,
                MarketDataErrors.QuoteCurrencyConflict.Code,
                MarketDataErrors.QuoteCurrencyConflict.Description,
                null,
                null,
                null,
                null);
        }

        IReadOnlyDictionary<Guid, PriceCollectionAttempt> attempts =
            await _observations.GetLatestAttemptsAsync(
                [instrument.Id],
                cancellationToken);
        attempts.TryGetValue(instrument.Id, out PriceCollectionAttempt? latestAttempt);

        if (session == ExchangeSessionStatus.Closed)
        {
            return WithAttempt(
                new CollectionStatusInfo(
                    InstrumentCollectionStatus.SessionClosed,
                    "price_collection.session_closed",
                    "The exchange session is currently closed.",
                    null,
                    null,
                    null,
                    null),
                latestAttempt,
                null);
        }

        Result<PriceObservationHistory> history = await _history.GetHistoryAsync(
            instrument.Id,
            null,
            1,
            cancellationToken);
        Instant? lastObservedAt = history.IsSuccess
            ? history.Value.Latest?.RetrievedAt
            : null;

        if (lastObservedAt is null)
        {
            return WithAttempt(
                new CollectionStatusInfo(
                    InstrumentCollectionStatus.AwaitingFirstObservation,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                latestAttempt,
                null);
        }

        return WithAttempt(
            new CollectionStatusInfo(
                InstrumentCollectionStatus.Collecting,
                null,
                null,
                lastObservedAt,
                null,
                null,
                null),
            latestAttempt,
            lastObservedAt);
    }

    private static CollectionStatusInfo WithAttempt(
        CollectionStatusInfo info,
        PriceCollectionAttempt? attempt,
        Instant? lastObservedAt)
    {
        return info with
        {
            LastObservedAt = lastObservedAt ?? info.LastObservedAt,
            LastAttemptAt = attempt?.RecordedAt,
            LastAttemptOutcome = attempt?.Outcome,
            LastAttemptErrorCode = attempt?.ErrorCode
        };
    }
}
