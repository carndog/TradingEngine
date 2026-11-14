using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Listings;

public sealed class ConfirmListingUnitHandler
{
    private readonly IWatchedInstrumentStore _instruments;
    private readonly IListingConfigurationStore _listings;
    private readonly IMarketDataProvider _provider;
    private readonly INativeQuoteUnitMap _quoteUnits;
    private readonly ListingStatusEvaluator _status;
    private readonly IClock _clock;

    public ConfirmListingUnitHandler(
        IWatchedInstrumentStore instruments,
        IListingConfigurationStore listings,
        IMarketDataProvider provider,
        INativeQuoteUnitMap quoteUnits,
        ListingStatusEvaluator status,
        IClock clock)
    {
        _instruments = instruments
            ?? throw new ArgumentNullException(nameof(instruments));
        _listings = listings ?? throw new ArgumentNullException(nameof(listings));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _quoteUnits = quoteUnits ?? throw new ArgumentNullException(nameof(quoteUnits));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<ListingStatusInfo>> HandleAsync(
        ConfirmListingUnit command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<WatchedInstrumentSettingsSnapshot> snapshot =
            await _instruments.GetSettingsAsync(
                command.InstrumentId,
                cancellationToken);
        if (snapshot.IsFailure)
        {
            return snapshot.Error;
        }

        WatchedInstrument instrument = snapshot.Value.Instrument;

        Result<MarketListing> resolved = await _provider.ResolveListingAsync(
            new MarketListingQuery(instrument.Exchange, instrument.Symbol),
            cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        MarketListing listing = resolved.Value;
        Result<string>? normalizedUnit = command.NativeQuoteCurrency is null
            ? null
            : InstrumentListing.NormalizeNativeUnit(command.NativeQuoteCurrency);
        if (normalizedUnit is { IsFailure: true })
        {
            return normalizedUnit.Error;
        }

        if (listing.NativeQuoteCurrency is not null
            && normalizedUnit is { IsSuccess: true }
            && string.Equals(
                listing.NativeQuoteCurrency,
                normalizedUnit.Value,
                StringComparison.Ordinal) is false)
        {
            return MarketDataErrors.QuoteCurrencyConflict
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("providerCurrency", listing.NativeQuoteCurrency)
                .WithExtension("requestedCurrency", normalizedUnit.Value);
        }

        string? effectiveUnit = listing.NativeQuoteCurrency ?? normalizedUnit?.Value;
        if (effectiveUnit is null)
        {
            return InstrumentListingErrors.NativeUnitRequired;
        }
        if (string.Equals(
                effectiveUnit,
                instrument.QuoteCurrency,
                StringComparison.Ordinal) is false)
        {
            return MarketDataErrors.QuoteCurrencyConflict
                .WithExtension("exchange", instrument.Exchange)
                .WithExtension("symbol", instrument.Symbol)
                .WithExtension("providerCurrency", effectiveUnit)
                .WithExtension("requestedCurrency", instrument.QuoteCurrency);
        }

        Result<NativeQuoteUnitResolution> sources = await _quoteUnits.ResolveAsync(
            instrument.Exchange,
            instrument.Symbol,
            cancellationToken);
        if (sources.IsFailure)
        {
            return sources.Error;
        }

        if (sources.Value.SourceConflict)
        {
            return SourceConflictError(instrument, sources.Value);
        }

        if (sources.Value.DeploymentCurrency is not null
            && string.Equals(
                sources.Value.DeploymentCurrency,
                effectiveUnit,
                StringComparison.Ordinal) is false)
        {
            return SourceConflictError(instrument, sources.Value);
        }

        Instant confirmedAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));
        ListingConfigurationUpsert upsert = new(
            instrument.Exchange,
            instrument.Symbol,
            listing.Provider,
            listing.ProviderInstrumentId,
            listing.ProviderSymbol,
            listing.ProviderExchangeName,
            listing.DisplayName,
            effectiveUnit,
            listing.NativeQuoteCurrency is not null
                ? ListingUnitSource.ProviderMetadata
                : ListingUnitSource.Administrator,
            command.ConfirmedBy,
            confirmedAt);

        Result<ListingConfigurationSnapshot> saved = await _listings.SaveAsync(
            upsert,
            command.ExpectedConcurrencyToken,
            cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        Result<ListingStatusInfo> status = await _status.EvaluateAsync(
            instrument.Exchange,
            instrument.Symbol,
            cancellationToken);
        if (status.IsFailure)
        {
            return status.Error;
        }

        return status.Value with
        {
            ResolutionState = ListingResolutionState.Resolved,
            ResolutionDetailCode = null
        };
    }

    private static Error SourceConflictError(
        WatchedInstrument instrument,
        NativeQuoteUnitResolution resolution)
    {
        return MarketDataErrors.QuoteCurrencySourceConflict
            .WithExtension("exchange", instrument.Exchange)
            .WithExtension("symbol", instrument.Symbol)
            .WithExtension("persistedCurrency", resolution.Persisted?.Listing.NativeQuoteCurrency)
            .WithExtension("deploymentCurrency", resolution.DeploymentCurrency);
    }
}
