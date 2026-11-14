using Microsoft.Extensions.Options;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.MarketData;

public sealed class CompositeNativeQuoteUnitMap : INativeQuoteUnitMap
{
    private readonly IListingConfigurationStore _listings;
    private readonly EToroMarketDataOptions _options;

    public CompositeNativeQuoteUnitMap(
        IListingConfigurationStore listings,
        IOptions<EToroMarketDataOptions> options)
    {
        _listings = listings ?? throw new ArgumentNullException(nameof(listings));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<Result<NativeQuoteUnitResolution>> ResolveAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken)
    {
        Result<ListingConfigurationSnapshot> persisted = await _listings.FindAsync(
            exchange,
            symbol,
            cancellationToken);
        if (persisted.IsFailure
            && persisted.Error.Code != Domain.Instruments.InstrumentListingErrors.NotFound.Code)
        {
            return persisted.Error;
        }

        string? deploymentCurrency = DeploymentCurrency(exchange, symbol);
        string? persistedCurrency = persisted.IsSuccess
            ? persisted.Value.Listing.NativeQuoteCurrency
            : null;

        if (persistedCurrency is not null
            && deploymentCurrency is not null
            && string.Equals(
                persistedCurrency,
                deploymentCurrency,
                StringComparison.OrdinalIgnoreCase) is false)
        {
            return new NativeQuoteUnitResolution(
                null,
                persisted.IsSuccess ? persisted.Value : null,
                deploymentCurrency,
                true);
        }

        return new NativeQuoteUnitResolution(
            persistedCurrency ?? deploymentCurrency,
            persisted.IsSuccess ? persisted.Value : null,
            deploymentCurrency,
            false);
    }

    private string? DeploymentCurrency(string exchange, string symbol)
    {
        if (_options.QuoteCurrencyMap.TryGetValue(
                exchange,
                out Dictionary<string, string>? symbols) is false
            || symbols.TryGetValue(symbol, out string? currency) is false
            || string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        return currency.Trim().ToUpperInvariant();
    }
}
