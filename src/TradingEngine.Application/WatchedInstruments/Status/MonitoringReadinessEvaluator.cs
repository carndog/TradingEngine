using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Status;

public sealed class MonitoringReadinessEvaluator
{
    private readonly INativeQuoteUnitMap _quoteUnits;
    private readonly IMarketSessionPolicy _sessionPolicy;
    private readonly IClock _clock;

    public MonitoringReadinessEvaluator(
        INativeQuoteUnitMap quoteUnits,
        IMarketSessionPolicy sessionPolicy,
        IClock clock)
    {
        _quoteUnits = quoteUnits ?? throw new ArgumentNullException(nameof(quoteUnits));
        _sessionPolicy = sessionPolicy
            ?? throw new ArgumentNullException(nameof(sessionPolicy));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result> EvaluateAsync(
        string exchange,
        string symbol,
        string quoteCurrency,
        CancellationToken cancellationToken,
        string? pendingNativeQuoteCurrency = null)
    {
        Result<NativeQuoteUnitResolution> resolved = await _quoteUnits.ResolveAsync(
            exchange,
            symbol,
            cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        NativeQuoteUnitResolution resolution = resolved.Value;
        if (resolution.SourceConflict)
        {
            return MarketDataErrors.QuoteCurrencySourceConflict
                .WithExtension("exchange", exchange)
                .WithExtension("symbol", symbol)
                .WithExtension("persistedCurrency", resolution.Persisted?.Listing.NativeQuoteCurrency)
                .WithExtension("deploymentCurrency", resolution.DeploymentCurrency);
        }

        string? nativeQuoteCurrency =
            pendingNativeQuoteCurrency ?? resolution.NativeQuoteCurrency;
        if (nativeQuoteCurrency is null)
        {
            return MarketDataErrors.QuoteCurrencyUnconfigured
                .WithExtension("exchange", exchange)
                .WithExtension("symbol", symbol)
                .WithExtension("requestedCurrency", quoteCurrency);
        }

        if (string.Equals(
                nativeQuoteCurrency,
                quoteCurrency,
                StringComparison.Ordinal) is false)
        {
            return MarketDataErrors.QuoteCurrencyConflict
                .WithExtension("exchange", exchange)
                .WithExtension("symbol", symbol)
                .WithExtension("providerCurrency", resolution.NativeQuoteCurrency)
                .WithExtension("requestedCurrency", quoteCurrency);
        }

        ExchangeSessionStatus session = _sessionPolicy.GetStatus(
            exchange,
            _clock.GetCurrentInstant());
        if (session == ExchangeSessionStatus.Unconfigured)
        {
            return PriceCollectionErrors.ExchangeSessionUnconfigured;
        }

        return Result.Success();
    }
}
