using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Verify;

public sealed class VerifyInstrumentHandler
{
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(20);
    private readonly IMarketDataProvider _provider;
    private readonly INativeQuoteUnitMap _units;
    private readonly MonitoringReadinessEvaluator _readiness;
    private readonly IMarketSessionPolicy _sessions;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;

    public VerifyInstrumentHandler(IMarketDataProvider provider, INativeQuoteUnitMap units,
        MonitoringReadinessEvaluator readiness, IMarketSessionPolicy sessions, IClock clock,
        TimeProvider timeProvider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _units = units ?? throw new ArgumentNullException(nameof(units));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<Result<InstrumentVerification>> HandleAsync(
        VerifyInstrument command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<WatchedInstrumentFields> validated = WatchedInstrument.Validate(
            command.Symbol, command.Exchange, command.QuoteCurrency, 60);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        Result<string>? requestedUnit = command.NativeQuoteCurrency is null
            ? null : InstrumentListing.NormalizeNativeUnit(command.NativeQuoteCurrency);
        if (requestedUnit is { IsFailure: true })
        {
            return requestedUnit.Error;
        }

        WatchedInstrumentFields fields = validated.Value;
        MarketInstrumentIdentity identity = new(fields.Exchange, fields.Symbol, fields.QuoteCurrency);
        MarketSessionDecision session = _sessions.GetDecision(identity.Exchange, _clock.GetCurrentInstant());
        using CancellationTokenSource budget = new(RequestBudget, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, budget.Token);
        InstrumentVerification result = new(identity, ListingResolutionState.Unavailable, null, null,
            null, MarketDataErrors.ProviderUnavailable.Code, session, null, null);

        try
        {
            Result<MarketListing> resolved = await _provider.ResolveListingAsync(
                new MarketListingQuery(identity.Exchange, identity.Symbol), linked.Token);
            if (resolved.IsFailure)
            {
                return result with
                {
                    ResolutionState = ListingStatusEvaluator.MapResolutionError(resolved.Error),
                    ResolutionDetailCode = resolved.Error.Code,
                    ReadinessDetailCode = resolved.Error.Code
                };
            }

            result = result with { ResolutionState = ListingResolutionState.Resolved, Listing = resolved.Value };
            Result<NativeQuoteUnitResolution> sources = await _units.ResolveAsync(
                identity.Exchange, identity.Symbol, linked.Token);
            if (sources.IsFailure)
            {
                return result with { ReadinessDetailCode = sources.Error.Code };
            }

            string? nativeUnit = resolved.Value.NativeQuoteCurrency ?? sources.Value.NativeQuoteCurrency;
            result = result with { NativeQuoteCurrency = nativeUnit };
            if (sources.Value.SourceConflict
                || (resolved.Value.NativeQuoteCurrency is not null && sources.Value.NativeQuoteCurrency is not null
                    && resolved.Value.NativeQuoteCurrency != sources.Value.NativeQuoteCurrency))
            {
                return result with { ReadinessDetailCode = MarketDataErrors.QuoteCurrencySourceConflict.Code };
            }

            if (nativeUnit is not null && requestedUnit is { IsSuccess: true } && requestedUnit.Value != nativeUnit)
            {
                return result with { ReadinessDetailCode = MarketDataErrors.QuoteCurrencyConflict.Code };
            }

            Result ready = await _readiness.EvaluateAsync(identity.Exchange, identity.Symbol,
                identity.QuoteCurrency, linked.Token, nativeUnit);
            result = result with { ReadinessDetailCode = ready.IsFailure ? ready.Error.Code : null };
            if (ready.IsFailure)
            {
                return result;
            }

            session = _sessions.GetDecision(identity.Exchange, _clock.GetCurrentInstant());
            result = result with { Session = session };
            if (session.Status != ExchangeSessionStatus.Open)
            {
                return result with { QuoteDetailCode = session.BlockingError?.Code };
            }

            Result<MarketQuote> quote = await _provider.GetCurrentQuoteAsync(identity, linked.Token);
            session = _sessions.GetDecision(identity.Exchange, _clock.GetCurrentInstant());
            result = result with { Session = session };
            if (session.Status != ExchangeSessionStatus.Open)
            {
                return result with { QuoteDetailCode = session.BlockingError?.Code };
            }

            if (quote.IsFailure)
            {
                return result with { QuoteDetailCode = quote.Error.Code };
            }

            if (quote.Value.Instrument != identity || quote.Value.Provider != resolved.Value.Provider
                || quote.Value.ProviderInstrumentId != resolved.Value.ProviderInstrumentId)
            {
                return result with { QuoteDetailCode = MarketDataErrors.ProviderUnavailable.Code };
            }

            if (quote.Value.Bid is null && quote.Value.Ask is null)
            {
                return result with { QuoteDetailCode = MarketDataErrors.PriceUnavailable.Code };
            }

            return result with { Quote = quote.Value };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            string code = exception is OperationCanceledException or TimeoutException
                ? MarketDataErrors.ProviderTimeout.Code : MarketDataErrors.ProviderUnavailable.Code;
            if (result.Listing is null)
            {
                return result with { ResolutionDetailCode = code, ReadinessDetailCode = code };
            }

            return result with { QuoteDetailCode = code };
        }
    }
}
