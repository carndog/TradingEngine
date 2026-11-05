using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.PriceCollection;

public sealed class CollectDuePricesHandler
{
    private const long TicksPerSecond = 10_000_000;

    private readonly IMonitoredInstrumentSource _source;
    private readonly IPriceObservationStore _store;
    private readonly IMarketDataProvider _marketData;
    private readonly ISamplingPolicy _samplingPolicy;
    private readonly IMarketSessionPolicy _sessionPolicy;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly PriceCollectionOptions _options;

    public CollectDuePricesHandler(
        IMonitoredInstrumentSource source,
        IPriceObservationStore store,
        IMarketDataProvider marketData,
        ISamplingPolicy samplingPolicy,
        IMarketSessionPolicy sessionPolicy,
        IClock clock,
        TimeProvider timeProvider,
        PriceCollectionOptions options)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        _samplingPolicy = samplingPolicy
            ?? throw new ArgumentNullException(nameof(samplingPolicy));
        _sessionPolicy = sessionPolicy
            ?? throw new ArgumentNullException(nameof(sessionPolicy));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PriceCollectionRunResult> HandleAsync(
        CollectDuePrices command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Instant startedAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));
        Guid invocationId = Guid.CreateVersion7();
        Instant deadline = startedAt.Plus(
            Duration.FromSeconds(_options.MaxExecutionTimeSeconds));

        using CancellationTokenSource budgetTimeout = new(
            TimeSpan.FromSeconds(_options.MaxExecutionTimeSeconds),
            _timeProvider);
        using CancellationTokenSource budgetScope =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                budgetTimeout.Token);
        CancellationToken budgetToken = budgetScope.Token;

        IReadOnlyList<MonitoredInstrument> instruments;
        try
        {
            instruments = await _source.ListMonitoredAsync(budgetToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return DeadlineResult(invocationId, startedAt, []);
        }

        List<PriceCollectionItemResult> items = [];
        if (instruments.Count == 0)
        {
            return new PriceCollectionRunResult(
                invocationId,
                startedAt,
                _clock.GetCurrentInstant(),
                false,
                items);
        }

        Guid[] instrumentIds = instruments
            .Select(instrument => instrument.Id)
            .ToArray();
        IReadOnlyDictionary<Guid, Instant> latestSlots;
        IReadOnlyDictionary<Guid, PriceCollectionAttempt> latestAttempts;
        try
        {
            latestSlots = await _store.GetLatestScheduledForAsync(
                instrumentIds,
                budgetToken);
            latestAttempts = await _store.GetLatestAttemptsAsync(
                instrumentIds,
                budgetToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return DeadlineResult(invocationId, startedAt, items);
        }

        bool deadlineReached = false;
        foreach (MonitoredInstrument instrument in instruments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Instant scheduledFor = SlotFloor(
                startedAt,
                _samplingPolicy.EffectiveIntervalSeconds(instrument, instruments.Count));

            if (latestSlots.TryGetValue(instrument.Id, out Instant latestSlot)
                && latestSlot >= scheduledFor)
            {
                continue;
            }

            latestAttempts.TryGetValue(instrument.Id, out PriceCollectionAttempt? latestAttempt);

            PriceCollectionItemResult item;
            try
            {
                item = await CollectInstrumentAsync(
                    instrument,
                    scheduledFor,
                    latestAttempt,
                    invocationId,
                    deadline,
                    budgetToken,
                    cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested is false)
            {
                item = await TryRecordStorageFailureAsync(
                    instrument,
                    scheduledFor,
                    invocationId,
                    cancellationToken);
            }

            items.Add(item);

            if (item.ErrorCode == PriceCollectionErrors.DeadlineExceeded.Code
                || _clock.GetCurrentInstant() >= deadline)
            {
                deadlineReached = true;
                break;
            }
        }

        return new PriceCollectionRunResult(
            invocationId,
            startedAt,
            _clock.GetCurrentInstant(),
            deadlineReached,
            items);
    }

    private PriceCollectionRunResult DeadlineResult(
        Guid invocationId,
        Instant startedAt,
        List<PriceCollectionItemResult> items)
    {
        return new PriceCollectionRunResult(
            invocationId,
            startedAt,
            _clock.GetCurrentInstant(),
            true,
            items);
    }

    private async Task<PriceCollectionItemResult> CollectInstrumentAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        PriceCollectionAttempt? latestAttempt,
        Guid invocationId,
        Instant deadline,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        Instant attemptedAt = _clock.GetCurrentInstant();

        if (attemptedAt >= deadline)
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.DeadlineExceeded.Code,
                null,
                null,
                cancellationToken);
        }

        if (latestAttempt is not null
            && latestAttempt.ScheduledFor == scheduledFor
            && latestAttempt.Outcome is PriceCollectionOutcome.Throttled
                or PriceCollectionOutcome.PriceUnavailable)
        {
            return new PriceCollectionItemResult(
                instrument.Id,
                scheduledFor,
                latestAttempt.Outcome,
                latestAttempt.ErrorCode,
                latestAttempt.ProviderRequestId);
        }

        ExchangeSessionStatus session =
            _sessionPolicy.GetStatus(instrument.Exchange, attemptedAt);
        if (session == ExchangeSessionStatus.Unconfigured)
        {
            return await RecordOutcomeAsync(
                instrument,
                scheduledFor,
                latestAttempt,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.ExchangeSessionUnconfigured.Code,
                null,
                null,
                cancellationToken);
        }

        if (session == ExchangeSessionStatus.Closed)
        {
            return await RecordOutcomeAsync(
                instrument,
                scheduledFor,
                latestAttempt,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.SkippedMarketClosed,
                null,
                null,
                null,
                cancellationToken);
        }

        if (_clock.GetCurrentInstant() >= deadline)
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.DeadlineExceeded.Code,
                null,
                null,
                cancellationToken);
        }

        MarketInstrumentIdentity identity = new(
            instrument.Exchange,
            instrument.Symbol,
            instrument.QuoteCurrency);

        MarketQuote? quote = null;
        for (int providerAttempt = 0;
            providerAttempt <= _options.UnavailableRetryAttempts;
            providerAttempt++)
        {
            Result<MarketQuote> result;
            try
            {
                result = await _marketData.GetCurrentQuoteAsync(
                    identity,
                    budgetToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested is false)
            {
                return await RecordAttemptAsync(
                    instrument,
                    scheduledFor,
                    attemptedAt,
                    invocationId,
                    PriceCollectionOutcome.Failed,
                    PriceCollectionErrors.DeadlineExceeded.Code,
                    null,
                    null,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                return await RecordAttemptAsync(
                    instrument,
                    scheduledFor,
                    attemptedAt,
                    invocationId,
                    PriceCollectionOutcome.Failed,
                    PriceCollectionErrors.ProviderTimeout.Code,
                    null,
                    null,
                    cancellationToken);
            }
            catch (Exception)
            {
                return await RecordAttemptAsync(
                    instrument,
                    scheduledFor,
                    attemptedAt,
                    invocationId,
                    PriceCollectionOutcome.Failed,
                    PriceCollectionErrors.ProviderError.Code,
                    null,
                    null,
                    cancellationToken);
            }

            if (result.IsFailure)
            {
                return await RecordAttemptAsync(
                    instrument,
                    scheduledFor,
                    attemptedAt,
                    invocationId,
                    result.Error.Code == MarketDataErrors.ProviderThrottled.Code
                        ? PriceCollectionOutcome.Throttled
                        : PriceCollectionOutcome.Failed,
                    result.Error.Code,
                    null,
                    null,
                    cancellationToken);
            }

            quote = result.Value;
            if (quote.Bid is not null || quote.Ask is not null)
            {
                return await RecordObservationAsync(
                    instrument,
                    scheduledFor,
                    attemptedAt,
                    invocationId,
                    identity,
                    quote,
                    budgetToken,
                    cancellationToken);
            }
        }

        return await RecordAttemptAsync(
            instrument,
            scheduledFor,
            attemptedAt,
            invocationId,
            PriceCollectionOutcome.PriceUnavailable,
            PriceCollectionErrors.PriceUnavailable.Code,
            quote?.Provider,
            quote?.ProviderRequestId,
            cancellationToken);
    }

    private async Task<PriceCollectionItemResult> RecordObservationAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        Instant attemptedAt,
        Guid invocationId,
        MarketInstrumentIdentity identity,
        MarketQuote value,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        ScheduledPriceObservation observation = new(
            instrument.Id,
            scheduledFor,
            value.Provider,
            value.ProviderInstrumentId,
            value.Bid,
            value.Ask,
            identity.QuoteCurrency,
            value.ProviderObservedAt,
            value.RetrievedAt,
            value.Timeliness,
            value.ProviderRequestId,
            invocationId);
        PriceCollectionAttempt observedAttempt = new(
            instrument.Id,
            scheduledFor,
            PriceCollectionOutcome.Observed,
            null,
            value.Provider,
            value.ProviderRequestId,
            invocationId,
            attemptedAt,
            _clock.GetCurrentInstant());

        bool recorded;
        try
        {
            recorded = await _store.TryRecordObservationAndAttemptAsync(
                observation,
                observedAttempt,
                budgetToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.DeadlineExceeded.Code,
                null,
                null,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.UnsupportedQuote.Code,
                value.Provider,
                value.ProviderRequestId,
                cancellationToken);
        }
        catch (PriceObservationStorageException)
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.StorageError.Code,
                value.Provider,
                value.ProviderRequestId,
                cancellationToken);
        }

        if (recorded is false)
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.SkippedDuplicate,
                null,
                value.Provider,
                value.ProviderRequestId,
                cancellationToken);
        }

        return new PriceCollectionItemResult(
            instrument.Id,
            scheduledFor,
            PriceCollectionOutcome.Observed,
            null,
            value.ProviderRequestId);
    }

    private async Task<PriceCollectionItemResult> RecordOutcomeAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        PriceCollectionAttempt? latestAttempt,
        Instant attemptedAt,
        Guid invocationId,
        PriceCollectionOutcome outcome,
        string? errorCode,
        string? provider,
        string? providerRequestId,
        CancellationToken cancellationToken)
    {
        if (latestAttempt is not null
            && latestAttempt.ScheduledFor == scheduledFor
            && latestAttempt.Outcome == outcome
            && latestAttempt.ErrorCode == errorCode)
        {
            return new PriceCollectionItemResult(
                instrument.Id,
                scheduledFor,
                outcome,
                errorCode,
                providerRequestId);
        }

        return await RecordAttemptAsync(
            instrument,
            scheduledFor,
            attemptedAt,
            invocationId,
            outcome,
            errorCode,
            provider,
            providerRequestId,
            cancellationToken);
    }

    private async Task<PriceCollectionItemResult> TryRecordStorageFailureAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        Guid invocationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                _clock.GetCurrentInstant(),
                invocationId,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.StorageError.Code,
                null,
                null,
                cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            return new PriceCollectionItemResult(
                instrument.Id,
                scheduledFor,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.StorageError.Code,
                null);
        }
    }

    private async Task<PriceCollectionItemResult> RecordAttemptAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        Instant attemptedAt,
        Guid invocationId,
        PriceCollectionOutcome outcome,
        string? errorCode,
        string? provider,
        string? providerRequestId,
        CancellationToken cancellationToken)
    {
        PriceCollectionAttempt attempt = new(
            instrument.Id,
            scheduledFor,
            outcome,
            errorCode,
            provider,
            providerRequestId,
            invocationId,
            attemptedAt,
            _clock.GetCurrentInstant());

        await _store.RecordAttemptAsync(attempt, cancellationToken);

        return new PriceCollectionItemResult(
            instrument.Id,
            scheduledFor,
            outcome,
            errorCode,
            providerRequestId);
    }

    private static Instant SlotFloor(Instant instant, int intervalSeconds)
    {
        long intervalTicks = intervalSeconds * TicksPerSecond;
        long ticks = instant.ToUnixTimeTicks();
        return Instant.FromUnixTimeTicks(ticks - ticks % intervalTicks);
    }
}
