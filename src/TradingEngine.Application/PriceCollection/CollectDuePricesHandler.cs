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
    private readonly INativeQuoteUnitMap _quoteUnits;
    private readonly ISamplingPolicy _samplingPolicy;
    private readonly IMarketSessionPolicy _sessionPolicy;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly PriceCollectionOptions _options;
    private readonly PriceCollectionProgress _progress;

    public CollectDuePricesHandler(
        IMonitoredInstrumentSource source,
        IPriceObservationStore store,
        IMarketDataProvider marketData,
        INativeQuoteUnitMap quoteUnits,
        ISamplingPolicy samplingPolicy,
        IMarketSessionPolicy sessionPolicy,
        IClock clock,
        TimeProvider timeProvider,
        PriceCollectionOptions options,
        PriceCollectionProgress? progress = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        _quoteUnits = quoteUnits ?? throw new ArgumentNullException(nameof(quoteUnits));
        _samplingPolicy = samplingPolicy
            ?? throw new ArgumentNullException(nameof(samplingPolicy));
        _sessionPolicy = sessionPolicy
            ?? throw new ArgumentNullException(nameof(sessionPolicy));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _progress = progress ?? new PriceCollectionProgress();
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
        MarketDataRequestStatistics initialRequests = _marketData.RequestStatistics;
        PriceCollectionRunCounters counters = new();
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
                items)
            {
                Diagnostics = new PriceCollectionRunDiagnostics { EligibilityScanCompleted = true }
            };
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

        int batchSize = Math.Max(1, _marketData.MaxBatchSize);
        Dictionary<Guid, Instant> dueSlots = instruments.ToDictionary(
            instrument => instrument.Id,
            instrument => SlotFloor(startedAt,
                _samplingPolicy.EffectiveIntervalSeconds(instrument, instruments.Count, batchSize)));
        foreach (Guid completed in dueSlots.Keys.Where(id =>
            latestSlots.TryGetValue(id, out Instant saved) && saved >= dueSlots[id]).ToArray())
        {
            dueSlots.Remove(completed);
        }

        Dictionary<Guid, Instant> openSlots = instruments
            .Where(instrument => dueSlots.ContainsKey(instrument.Id)
                && _sessionPolicy.GetStatus(instrument.Exchange, startedAt) == ExchangeSessionStatus.Open)
            .ToDictionary(instrument => instrument.Id, instrument => dueSlots[instrument.Id]);
        int expiredSlots = _progress.TrackDueSlots(openSlots, latestSlots, instrumentIds);
        List<PendingPriceCollection> pending = [];
        bool deadlineReached = false;
        int preparedInstruments = 0;
        Guid? interruptedInstrument = _progress.InterruptedInstrument;
        foreach (MonitoredInstrument instrument in _progress.Order(instruments))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (dueSlots.TryGetValue(instrument.Id, out Instant scheduledFor) is false)
            {
                continue;
            }

            if (instrument.Id == interruptedInstrument && pending.Count > 0)
            {
                items.AddRange(await CollectBatchAsync(
                    pending, invocationId, deadline, budgetToken, cancellationToken, counters));
                pending.Clear();
                if (budgetToken.IsCancellationRequested || _clock.GetCurrentInstant() >= deadline
                    || items.Any(result => result.ErrorCode == PriceCollectionErrors.DeadlineExceeded.Code))
                {
                    deadlineReached = true;
                    break;
                }
            }

            _progress.AdvancePast(instrument.Id);
            latestAttempts.TryGetValue(instrument.Id, out PriceCollectionAttempt? latestAttempt);

            PriceCollectionItemResult? item;
            Instant attemptedAt = _clock.GetCurrentInstant();
            try
            {
                item = await PrepareInstrumentAsync(
                    instrument,
                    scheduledFor,
                    latestAttempt,
                    invocationId,
                    deadline,
                    attemptedAt,
                    budgetToken,
                    cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested is false)
            {
                item = budgetToken.IsCancellationRequested || _clock.GetCurrentInstant() >= deadline
                    ? new PriceCollectionItemResult(instrument.Id, scheduledFor, PriceCollectionOutcome.Failed,
                        PriceCollectionErrors.DeadlineExceeded.Code, null)
                    : await TryRecordStorageFailureAsync(
                    instrument,
                    scheduledFor,
                    invocationId,
                    budgetToken,
                    cancellationToken);
            }

            if (item?.ErrorCode != PriceCollectionErrors.DeadlineExceeded.Code)
            {
                preparedInstruments++;
            }

            if (item is not null)
            {
                items.Add(item);
            }
            else
            {
                counters.Eligible.Add(instrument.Id);
                pending.Add(new PendingPriceCollection(instrument, scheduledFor, attemptedAt,
                    scheduledFor.Plus(Duration.FromSeconds(_samplingPolicy.EffectiveIntervalSeconds(
                        instrument, instruments.Count, batchSize))), latestAttempt));
            }

            if (pending.Count == batchSize)
            {
                items.AddRange(await CollectBatchAsync(
                    pending, invocationId, deadline, budgetToken, cancellationToken, counters));
                pending.Clear();
            }

            if (item?.ErrorCode == PriceCollectionErrors.DeadlineExceeded.Code
                || budgetToken.IsCancellationRequested
                || items.LastOrDefault()?.ErrorCode == PriceCollectionErrors.DeadlineExceeded.Code
                || _clock.GetCurrentInstant() >= deadline)
            {
                deadlineReached = true;
                break;
            }
        }

        if (pending.Count > 0)
        {
            items.AddRange(await CollectBatchAsync(
                pending, invocationId, deadline, budgetToken, cancellationToken, counters));
        }

        deadlineReached |= budgetToken.IsCancellationRequested
            || _clock.GetCurrentInstant() >= deadline
            || items.Any(item => item.ErrorCode == PriceCollectionErrors.DeadlineExceeded.Code);

        _progress.FinishRun(deadlineReached);
        return new PriceCollectionRunResult(
            invocationId,
            startedAt,
            _clock.GetCurrentInstant(),
            deadlineReached,
            items)
        {
            Diagnostics = new PriceCollectionRunDiagnostics
            {
                Monitored = instruments.Count,
                Due = dueSlots.Count,
                Eligible = counters.Eligible.Count,
                Requested = counters.Requested.Count,
                Returned = counters.Returned.Count,
                Persisted = items.Count(item => item.Outcome == PriceCollectionOutcome.Observed),
                Deferred = items.Count(IsDeferred),
                Failed = items.Count(item => item.Outcome == PriceCollectionOutcome.Failed && IsDeferred(item) is false),
                Unprocessed = dueSlots.Count - items.Select(item => item.InstrumentId).Distinct().Count(),
                ExpiredPendingSlots = expiredSlots + items.Count(item => item.ErrorCode == PriceCollectionErrors.SlotExpired.Code),
                EligibilityScanCompleted = preparedInstruments == dueSlots.Count,
                Requests = _marketData.RequestStatistics.Since(initialRequests)
            }
        };
    }

    private static bool IsDeferred(PriceCollectionItemResult item)
    {
        return item.ErrorCode == PriceCollectionErrors.DeadlineExceeded.Code
            || item.ErrorCode == MarketDataErrors.ProviderRequestDeferred.Code;
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

    private async Task<PriceCollectionItemResult?> PrepareInstrumentAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        PriceCollectionAttempt? latestAttempt,
        Guid invocationId,
        Instant deadline,
        Instant attemptedAt,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
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
                budgetToken,
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

        MarketSessionDecision decision = _sessionPolicy.GetDecision(instrument.Exchange, attemptedAt);
        ExchangeSessionStatus session = decision.Status;
        if (session is ExchangeSessionStatus.Unconfigured or ExchangeSessionStatus.CalendarUnavailable)
        {
            return await RecordOutcomeAsync(
                instrument,
                scheduledFor,
                latestAttempt,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                decision.ReasonCode,
                null,
                null,
                budgetToken,
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
                decision.ReasonCode,
                null,
                null,
                budgetToken,
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
                budgetToken,
                cancellationToken);
        }

        string? unitError = await ResolveUnitErrorAsync(
            instrument,
            budgetToken,
            cancellationToken);
        if (unitError is not null)
        {
            return await RecordOutcomeAsync(
                instrument,
                scheduledFor,
                latestAttempt,
                attemptedAt,
                invocationId,
                PriceCollectionOutcome.Failed,
                unitError,
                null,
                null,
                budgetToken,
                cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<PriceCollectionItemResult>> CollectBatchAsync(
        IReadOnlyList<PendingPriceCollection> prepared,
        Guid invocationId,
        Instant deadline,
        CancellationToken budgetToken,
        CancellationToken cancellationToken,
        PriceCollectionRunCounters counters)
    {
        List<PriceCollectionItemResult> items = [];
        List<PendingPriceCollection> pending = prepared.ToList();
        for (int attempt = 0; pending.Count > 0; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> quotes =
                await FetchBatchAsync(pending, deadline, budgetToken, cancellationToken, counters);
            List<PendingPriceCollection> retry = [];
            foreach (PendingPriceCollection item in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Result<MarketQuote> result = quotes.TryGetValue(item.Identity, out Result<MarketQuote>? quote)
                    ? quote
                    : MarketDataErrors.PriceUnavailable;
                if (result.IsSuccess && (result.Value.Bid is not null || result.Value.Ask is not null))
                {
                    counters.Returned.Add(item.Instrument.Id);
                }

                bool unavailable = result.IsFailure
                    ? result.Error.Code == MarketDataErrors.PriceUnavailable.Code
                    : result.Value.Bid is null && result.Value.Ask is null;
                if (unavailable && attempt < _options.UnavailableRetryAttempts
                    && budgetToken.IsCancellationRequested is false
                    && _clock.GetCurrentInstant() < deadline)
                {
                    retry.Add(item);
                    continue;
                }

                try
                {
                    if (budgetToken.IsCancellationRequested is false && _clock.GetCurrentInstant() < deadline
                        && (result.IsSuccess || result.Error.Code != PriceCollectionErrors.DeadlineExceeded.Code))
                    {
                        _progress.AdvancePast(item.Instrument.Id);
                    }

                    PriceCollectionItemResult recorded = await RecordBatchItemAsync(
                        item, result, invocationId, budgetToken, cancellationToken);
                    items.Add(recorded);
                    if (recorded.Outcome is PriceCollectionOutcome.Observed or PriceCollectionOutcome.SkippedDuplicate)
                    {
                        _progress.Complete(item.Instrument.Id);
                    }
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested is false)
                {
                    items.Add(await TryRecordStorageFailureAsync(
                        item.Instrument, item.ScheduledFor, invocationId, budgetToken, cancellationToken));
                }
            }

            pending = retry;
        }

        return items;
    }

    private async Task<IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>>> FetchBatchAsync(
        IReadOnlyList<PendingPriceCollection> pending,
        Instant deadline,
        CancellationToken budgetToken,
        CancellationToken cancellationToken,
        PriceCollectionRunCounters counters)
    {
        Dictionary<MarketInstrumentIdentity, Instant> expiry = pending.GroupBy(item => item.Identity)
            .ToDictionary(group => group.Key, group => group.Min(item => item.ExpiresAt));
        PriceCollectionRequestGuard guard = new(_sessionPolicy, _clock, deadline, expiry);
        IReadOnlyDictionary<MarketInstrumentIdentity, Error> blocked = guard.GetBlocked(expiry.Keys.ToArray());
        Dictionary<MarketInstrumentIdentity, Result<MarketQuote>> results = blocked
            .ToDictionary(entry => entry.Key, entry => Result<MarketQuote>.Failure(entry.Value));
        MarketInstrumentIdentity[] identities = expiry.Keys.Where(identity => blocked.ContainsKey(identity) is false).ToArray();
        if (identities.Length == 0)
        {
            return results;
        }

        Error error;
        try
        {
            if (_clock.GetCurrentInstant() < deadline && budgetToken.IsCancellationRequested is false)
            {
                _progress.AdvancePast(pending.First(item => blocked.ContainsKey(item.Identity) is false).Instrument.Id);
                counters.Requested.UnionWith(pending.Where(item => blocked.ContainsKey(item.Identity) is false)
                    .Select(item => item.Instrument.Id));
                IReadOnlyDictionary<MarketInstrumentIdentity, Result<MarketQuote>> quotes =
                    await _marketData.GetCurrentQuotesAsync(identities, budgetToken, guard);
                foreach (KeyValuePair<MarketInstrumentIdentity, Result<MarketQuote>> quote in quotes)
                {
                    results[quote.Key] = quote.Value;
                }

                return results;
            }

            error = PriceCollectionErrors.DeadlineExceeded;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            error = PriceCollectionErrors.DeadlineExceeded;
        }
        catch (TimeoutException)
        {
            error = PriceCollectionErrors.ProviderTimeout;
        }
        catch (Exception)
        {
            error = PriceCollectionErrors.ProviderError;
        }

        foreach (MarketInstrumentIdentity identity in identities)
        {
            results[identity] = error;
        }

        return results;
    }

    private async Task<PriceCollectionItemResult> RecordBatchItemAsync(
        PendingPriceCollection item,
        Result<MarketQuote> result,
        Guid invocationId,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        if (result.IsFailure && result.Error.Code == PriceCollectionErrors.SlotExpired.Code)
        {
            _progress.Complete(item.Instrument.Id);
        }

        if (result.IsSuccess && result.Value.RetrievedAt >= item.ExpiresAt)
        {
            _progress.Complete(item.Instrument.Id);
            return await RecordAttemptAsync(item.Instrument, item.ScheduledFor, item.AttemptedAt,
                invocationId, PriceCollectionOutcome.Failed, PriceCollectionErrors.SlotExpired.Code,
                result.Value.Provider, result.Value.ProviderRequestId, budgetToken, cancellationToken);
        }

        if (result.IsSuccess && (result.Value.Bid is not null || result.Value.Ask is not null))
        {
            return await RecordObservationAsync(
                item.Instrument, item.ScheduledFor, item.AttemptedAt, invocationId,
                item.Identity, result.Value, budgetToken, cancellationToken);
        }

        (PriceCollectionOutcome outcome, string errorCode) = GetUnobservedOutcome(result);
        return await RecordOutcomeAsync(
            item.Instrument,
            item.ScheduledFor,
            item.LatestAttempt,
            item.AttemptedAt,
            invocationId,
            outcome,
            errorCode,
            result.IsSuccess ? result.Value.Provider : null,
            result.IsSuccess ? result.Value.ProviderRequestId : null,
            budgetToken,
            cancellationToken);
    }

    private static (PriceCollectionOutcome Outcome, string ErrorCode) GetUnobservedOutcome(Result<MarketQuote> result)
    {
        if (result.IsSuccess || result.Error.Code == MarketDataErrors.PriceUnavailable.Code)
        {
            return (PriceCollectionOutcome.PriceUnavailable, PriceCollectionErrors.PriceUnavailable.Code);
        }

        Error error = result.Error;
        if (error.Code == MarketDataErrors.ProviderThrottled.Code)
        {
            return (PriceCollectionOutcome.Throttled, error.Code);
        }

        if (MarketSessionDecision.IsClosedError(error.Code))
        {
            return (PriceCollectionOutcome.SkippedMarketClosed, error.Code);
        }

        string errorCode = error.Code == MarketDataErrors.ProviderTimeout.Code
            ? PriceCollectionErrors.ProviderTimeout.Code
            : error.Code;
        return (PriceCollectionOutcome.Failed, errorCode);
    }

    private async Task<string?> ResolveUnitErrorAsync(
        MonitoredInstrument instrument,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        Result<NativeQuoteUnitResolution> resolved;
        try
        {
            resolved = await _quoteUnits.ResolveAsync(
                instrument.Exchange,
                instrument.Symbol,
                budgetToken);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PriceCollectionErrors.DeadlineExceeded.Code;
        }

        if (resolved.IsFailure)
        {
            return resolved.Error.Code;
        }

        NativeQuoteUnitResolution resolution = resolved.Value;
        if (resolution.SourceConflict)
        {
            return MarketDataErrors.QuoteCurrencySourceConflict.Code;
        }

        if (resolution.NativeQuoteCurrency is null)
        {
            return MarketDataErrors.QuoteCurrencyUnconfigured.Code;
        }

        if (string.Equals(
                resolution.NativeQuoteCurrency,
                instrument.QuoteCurrency,
                StringComparison.Ordinal) is false)
        {
            return MarketDataErrors.QuoteCurrencyConflict.Code;
        }

        return null;
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
                budgetToken,
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
                budgetToken,
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
                budgetToken,
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
                budgetToken,
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
        CancellationToken budgetToken,
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
            budgetToken,
            cancellationToken);
    }

    private async Task<PriceCollectionItemResult> TryRecordStorageFailureAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        Guid invocationId,
        CancellationToken budgetToken,
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
                budgetToken,
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
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        if (budgetToken.IsCancellationRequested)
        {
            return new PriceCollectionItemResult(
                instrument.Id,
                scheduledFor,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.DeadlineExceeded.Code,
                null);
        }

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

        try
        {
            await _store.RecordAttemptAsync(attempt, budgetToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return new PriceCollectionItemResult(
                instrument.Id,
                scheduledFor,
                PriceCollectionOutcome.Failed,
                PriceCollectionErrors.DeadlineExceeded.Code,
                null);
        }

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
