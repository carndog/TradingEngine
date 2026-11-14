using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Status;

public sealed class ListingStatusEvaluator
{
    private readonly INativeQuoteUnitMap _quoteUnits;

    public ListingStatusEvaluator(INativeQuoteUnitMap quoteUnits)
    {
        _quoteUnits = quoteUnits ?? throw new ArgumentNullException(nameof(quoteUnits));
    }

    public async Task<Result<ListingStatusInfo>> EvaluateAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken)
    {
        Result<NativeQuoteUnitResolution> resolved = await _quoteUnits.ResolveAsync(
            exchange,
            symbol,
            cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        return Compose(resolved.Value, null, null);
    }

    public static ListingStatusInfo Compose(
        NativeQuoteUnitResolution resolution,
        ListingResolutionState? resolutionState,
        string? resolutionDetailCode)
    {
        InstrumentListing? listing = resolution.Persisted?.Listing;
        byte[]? token = resolution.Persisted?.ConcurrencyToken;
        bool deploymentConfigured = resolution.DeploymentCurrency is not null;

        ListingUnitState unitState = ResolveUnitState(resolution);
        string? nativeUnit = listing?.NativeQuoteCurrency ?? resolution.DeploymentCurrency;
        ListingUnitSource? source = listing?.Source
            ?? (deploymentConfigured ? ListingUnitSource.Deployment : null);

        return new ListingStatusInfo(
            unitState,
            nativeUnit,
            source,
            deploymentConfigured,
            listing,
            token,
            resolutionState,
            resolutionDetailCode);
    }

    private static ListingUnitState ResolveUnitState(NativeQuoteUnitResolution resolution)
    {
        if (resolution.SourceConflict)
        {
            return ListingUnitState.Conflict;
        }

        if (resolution.Persisted is not null)
        {
            return ListingUnitState.Confirmed;
        }

        if (resolution.DeploymentCurrency is not null)
        {
            return ListingUnitState.Deployment;
        }

        return ListingUnitState.Unconfirmed;
    }
}
