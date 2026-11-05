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
    private readonly PriceCollectionOptions _options;

    public CollectDuePricesHandler(
        IMonitoredInstrumentSource source,
        IPriceObservationStore store,
        IMarketDataProvider marketData,
        ISamplingPolicy samplingPolicy,
        IMarketSessionPolicy sessionPolicy,
        IClock clock,
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

        IReadOnlyList<MonitoredInstrument> instruments =
            await _source.ListMonitoredAsync(cancellationToken);

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
        IReadOnlyDictionary<Guid, Instant> latestSlots =
            await _store.GetLatestScheduledForAsync(instrumentIds, cancellationToken);

        bool deadlineReached = false;
        using CancellationTokenSource deadlineScope =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineScope.CancelAfter(TimeSpan.FromSeconds(_options.MaxExecutionTimeSeconds));

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

            PriceCollectionItemResult item = await CollectInstrumentAsync(
                instrument,
                scheduledFor,
                invocationId,
                deadline,
                deadlineScope.Token,
                cancellationToken);
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

    private async Task<PriceCollectionItemResult> CollectInstrumentAsync(
        MonitoredInstrument instrument,
        Instant scheduledFor,
        Guid invocationId,
        Instant deadline,
        CancellationToken runToken,
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

        ExchangeSessionStatus session =
            _sessionPolicy.GetStatus(instrument.Exchange, attemptedAt);
        if (session == ExchangeSessionStatus.Unconfigured)
        {
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
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
            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
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

        try
        {
            Result<MarketQuote> quote = await _marketData.GetCurrentQuoteAsync(
                identity,
                runToken);

            if (quote.IsFailure)
            {
                return await RecordAttemptAsync(
                    instrument,
                    scheduledFor,
                    attemptedAt,
                    invocationId,
                    quote.Error.Code == MarketDataErrors.ProviderThrottled.Code
                        ? PriceCollectionOutcome.Throttled
                        : PriceCollectionOutcome.Failed,
                    quote.Error.Code,
                    null,
                    null,
                    cancellationToken);
            }

            MarketQuote value = quote.Value;
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
            bool recorded = await _store.TryRecordObservationAsync(
                observation,
                cancellationToken);

            return await RecordAttemptAsync(
                instrument,
                scheduledFor,
                attemptedAt,
                invocationId,
                recorded
                    ? PriceCollectionOutcome.Observed
                    : PriceCollectionOutcome.SkippedDuplicate,
                null,
                value.Provider,
                value.ProviderRequestId,
                cancellationToken);
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
