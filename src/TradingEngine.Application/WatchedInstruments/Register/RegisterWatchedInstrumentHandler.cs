using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed class RegisterWatchedInstrumentHandler
{
    private readonly IClock _clock;
    private readonly IWatchedInstrumentStore _store;
    private readonly IMarketDataProvider _provider;
    private readonly IListingConfigurationStore _listings;
    private readonly INativeQuoteUnitMap _quoteUnits;
    private readonly MonitoringReadinessEvaluator _readiness;

    public RegisterWatchedInstrumentHandler(
        IClock clock,
        IWatchedInstrumentStore store,
        IMarketDataProvider provider,
        IListingConfigurationStore listings,
        INativeQuoteUnitMap quoteUnits,
        MonitoringReadinessEvaluator readiness)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _listings = listings ?? throw new ArgumentNullException(nameof(listings));
        _quoteUnits = quoteUnits ?? throw new ArgumentNullException(nameof(quoteUnits));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
    }

    public async Task<Result<WatchedInstrumentRegistrationOutcome>> HandleAsync(
        RegisterWatchedInstrument command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ConfirmedBy);

        Instant occurredAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));
        Result<WatchedInstrumentFields> fields = WatchedInstrument.Validate(
            command.Symbol,
            command.Exchange,
            command.QuoteCurrency,
            command.SamplingIntervalSeconds);

        if (fields.IsFailure)
        {
            return fields.Error;
        }

        if (Enum.IsDefined(command.MonitoringState) is false)
        {
            return WatchedInstrumentErrors.MonitoringStateUndefined;
        }

        Result<string>? adminUnit = command.NativeQuoteCurrency is null
            ? null
            : InstrumentListing.NormalizeNativeUnit(command.NativeQuoteCurrency);
        if (adminUnit is { IsFailure: true })
        {
            return adminUnit.Error;
        }

        Result<MarketListing> resolved = await _provider.ResolveListingAsync(
            new MarketListingQuery(fields.Value.Exchange, fields.Value.Symbol),
            cancellationToken);

        ListingResolutionState resolutionState;
        string? resolutionDetail = null;
        MarketListing? listing = null;
        if (resolved.IsFailure)
        {
            resolutionState = MapResolutionError(resolved.Error);
            resolutionDetail = resolved.Error.Code;
        }
        else
        {
            resolutionState = ListingResolutionState.Resolved;
            listing = resolved.Value;
        }

        Result<NativeQuoteUnitResolution> sources = await _quoteUnits.ResolveAsync(
            fields.Value.Exchange,
            fields.Value.Symbol,
            cancellationToken);
        if (sources.IsFailure)
        {
            return sources.Error;
        }

        NativeQuoteUnitResolution resolution = sources.Value;
        if (resolution.SourceConflict)
        {
            return SourceConflictError(resolution);
        }

        if (listing?.NativeQuoteCurrency is not null
            && adminUnit is { IsSuccess: true }
            && string.Equals(
                listing.NativeQuoteCurrency,
                adminUnit.Value,
                StringComparison.Ordinal) is false)
        {
            return QuoteCurrencyConflict(
                fields.Value,
                listing.NativeQuoteCurrency,
                adminUnit.Value);
        }

        string? effectiveUnit = listing?.NativeQuoteCurrency ?? adminUnit?.Value;
        if (effectiveUnit is not null
            && resolution.DeploymentCurrency is not null
            && string.Equals(
                resolution.DeploymentCurrency,
                effectiveUnit,
                StringComparison.Ordinal) is false)
        {
            return SourceConflictError(resolution);
        }

        if (effectiveUnit is not null
            && resolution.Persisted is not null
            && string.Equals(
                resolution.Persisted.Listing.NativeQuoteCurrency,
                effectiveUnit,
                StringComparison.Ordinal) is false)
        {
            return InstrumentListingErrors.UnitConflict
                .WithExtension("exchange", fields.Value.Exchange)
                .WithExtension("symbol", fields.Value.Symbol)
                .WithExtension("confirmedCurrency", resolution.Persisted.Listing.NativeQuoteCurrency)
                .WithExtension("requestedCurrency", effectiveUnit);
        }

        if (effectiveUnit is not null
            && string.Equals(
                effectiveUnit,
                fields.Value.QuoteCurrency,
                StringComparison.Ordinal) is false)
        {
            return QuoteCurrencyConflict(
                fields.Value,
                effectiveUnit,
                fields.Value.QuoteCurrency);
        }

        ListingConfigurationSnapshot? persisted = resolution.Persisted;
        if (effectiveUnit is not null && listing is not null)
        {
            Result<ListingConfigurationSnapshot> saved = await _listings.SaveAsync(
                new ListingConfigurationUpsert(
                    fields.Value.Exchange,
                    fields.Value.Symbol,
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
                    occurredAt),
                persisted?.ConcurrencyToken,
                cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            persisted = saved.Value;
        }

        if (command.MonitoringState == MonitoringState.Monitored)
        {
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            Result ready = await _readiness.EvaluateAsync(
                fields.Value.Exchange,
                fields.Value.Symbol,
                fields.Value.QuoteCurrency,
                cancellationToken);
            if (ready.IsFailure)
            {
                return ready.Error;
            }
        }

        Result<Guid> stored = await _store.AddAsync(
            new WatchedInstrumentRegistration(
                fields.Value,
                command.MonitoringState,
                occurredAt,
                command.Definition),
            cancellationToken);

        if (stored.IsFailure)
        {
            return stored.Error;
        }

        Result<WatchedInstrument> instrument = WatchedInstrument.Restore(
            stored.Value,
            fields.Value.Symbol,
            fields.Value.Exchange,
            fields.Value.QuoteCurrency,
            command.MonitoringState,
            fields.Value.SamplingIntervalSeconds,
            occurredAt,
            occurredAt);
        if (instrument.IsFailure)
        {
            return instrument.Error;
        }

        NativeQuoteUnitResolution finalResolution = new(
            persisted?.Listing.NativeQuoteCurrency ?? resolution.DeploymentCurrency,
            persisted,
            resolution.DeploymentCurrency,
            false);
        ListingStatusInfo listingInfo = ListingStatusEvaluator.Compose(
            finalResolution,
            resolutionState,
            resolutionDetail);

        return new WatchedInstrumentRegistrationOutcome(instrument.Value, listingInfo);
    }

    private static ListingResolutionState MapResolutionError(Error error)
    {
        if (error.Code == MarketDataErrors.InstrumentMappingUnresolved.Code)
        {
            return ListingResolutionState.NotFound;
        }

        if (error.Code == MarketDataErrors.InstrumentMappingAmbiguous.Code)
        {
            return ListingResolutionState.Ambiguous;
        }

        if (error.Code == MarketDataErrors.ExchangeMappingUnconfigured.Code)
        {
            return ListingResolutionState.UnsupportedVenue;
        }

        return ListingResolutionState.Unavailable;
    }

    private static Error SourceConflictError(NativeQuoteUnitResolution resolution)
    {
        return MarketDataErrors.QuoteCurrencySourceConflict
            .WithExtension("persistedCurrency", resolution.Persisted?.Listing.NativeQuoteCurrency)
            .WithExtension("deploymentCurrency", resolution.DeploymentCurrency);
    }

    private static Error QuoteCurrencyConflict(
        WatchedInstrumentFields fields,
        string providerCurrency,
        string requestedCurrency)
    {
        return MarketDataErrors.QuoteCurrencyConflict
            .WithExtension("exchange", fields.Exchange)
            .WithExtension("symbol", fields.Symbol)
            .WithExtension("providerCurrency", providerCurrency)
            .WithExtension("requestedCurrency", requestedCurrency);
    }
}
