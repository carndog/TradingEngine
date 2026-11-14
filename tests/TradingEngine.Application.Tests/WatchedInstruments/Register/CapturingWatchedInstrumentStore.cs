using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

internal sealed class CapturingWatchedInstrumentStore : IWatchedInstrumentStore
{
    internal static readonly Guid DefaultGeneratedId =
        Guid.Parse("a34b2207-fc21-4226-91b2-47eb4a40bde1");

    private readonly Result<Guid> _addResult;
    private readonly Result<WatchedInstrumentConfiguration>? _getResult;

    public CapturingWatchedInstrumentStore()
        : this(DefaultGeneratedId, null)
    {
    }

    public CapturingWatchedInstrumentStore(Result<Guid> addResult)
        : this(addResult, null)
    {
    }

    public CapturingWatchedInstrumentStore(
        Result<Guid> addResult,
        Result<WatchedInstrumentConfiguration>? getResult)
    {
        _addResult = addResult;
        _getResult = getResult;
    }

    public WatchedInstrumentRegistration? AddedRegistration { get; private set; }

    public Guid? RequestedInstrumentId { get; private set; }

    public Instant? RequestedAt { get; private set; }

    public Result<WatchedInstrumentPage>? SearchResult { get; set; }

    public Result<WatchedInstrumentSettingsSnapshot>? GetSettingsResult { get; set; }

    public Result<WatchedInstrumentSettingsSnapshot>? SaveSettingsResult { get; set; }

    public WatchedInstrument? SavedInstrument { get; private set; }

    public PriceDisplayPreferences? SavedDisplay { get; private set; }

    public byte[]? ExpectedToken { get; private set; }

    public (string? Query, int Page, int PageSize)? RequestedSearch { get; private set; }

    public Task<Result<WatchedInstrumentRegistrationReceipt>> AddAsync(
        WatchedInstrumentRegistration registration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddedRegistration = registration;
        if (_addResult.IsFailure)
        {
            return Task.FromResult<Result<WatchedInstrumentRegistrationReceipt>>(
                _addResult.Error);
        }

        return Task.FromResult<Result<WatchedInstrumentRegistrationReceipt>>(
            new WatchedInstrumentRegistrationReceipt(
                _addResult.Value,
                SimulateListing(registration)));
    }

    private static ListingConfigurationSnapshot? SimulateListing(
        WatchedInstrumentRegistration registration)
    {
        if (registration.Listing is null)
        {
            return null;
        }

        ListingConfigurationUpsert upsert = registration.Listing;
        Result<InstrumentListing> listing = InstrumentListing.Restore(
            upsert.Exchange,
            upsert.Symbol,
            upsert.Provider,
            upsert.ProviderInstrumentId,
            upsert.ProviderSymbol,
            upsert.ProviderExchangeName,
            upsert.DisplayName,
            upsert.NativeQuoteCurrency,
            upsert.Source,
            upsert.ConfirmedBy,
            upsert.ConfirmedAt,
            upsert.ConfirmedAt);

        return new ListingConfigurationSnapshot(listing.Value, [9, 9, 9, 9, 9, 9, 9, 9]);
    }

    public Task<Result<WatchedInstrumentConfiguration>> GetAsync(
        Guid instrumentId,
        Instant at,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedInstrumentId = instrumentId;
        RequestedAt = at;

        return Task.FromResult(_getResult ?? WatchedInstrumentErrors.ConfigurationNotFound);
    }

    public Task<Result<WatchedInstrumentPage>> SearchAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedSearch = (query, page, pageSize);

        return Task.FromResult(
            SearchResult ?? new WatchedInstrumentPage([], page, pageSize, 0));
    }

    public Task<Result<WatchedInstrumentSettingsSnapshot>> GetSettingsAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            GetSettingsResult ?? WatchedInstrumentErrors.SettingsNotFound);
    }

    public Task<Result<WatchedInstrumentSettingsSnapshot>> SaveSettingsAsync(
        WatchedInstrument instrument,
        PriceDisplayPreferences display,
        byte[] expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedInstrument = instrument;
        SavedDisplay = display;
        ExpectedToken = expectedConcurrencyToken;

        if (SaveSettingsResult is not null)
        {
            return Task.FromResult(SaveSettingsResult);
        }

        return Task.FromResult<Result<WatchedInstrumentSettingsSnapshot>>(
            new WatchedInstrumentSettingsSnapshot(instrument, [7, 7, 7, 7, 7, 7, 7, 7])
            {
                Display = display
            });
    }

    public Task<Result<WatchedInstrumentBulkDeleteOutcome>> DeleteInstrumentsAsync(
        IReadOnlyList<WatchedInstrumentDeleteItem> items,
        Instant deletedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedDelete = (items, deletedAt);

        return Task.FromResult(
            DeleteResult ?? new WatchedInstrumentBulkDeleteOutcome(
                items.Select(item => item.InstrumentId).ToArray(),
                []));
    }

    public (IReadOnlyList<WatchedInstrumentDeleteItem> Items, Instant DeletedAt)? RequestedDelete { get; private set; }

    public Result<WatchedInstrumentBulkDeleteOutcome>? DeleteResult { get; set; }
}
