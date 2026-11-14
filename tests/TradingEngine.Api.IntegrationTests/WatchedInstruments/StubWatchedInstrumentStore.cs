using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

internal sealed class StubWatchedInstrumentStore : IWatchedInstrumentStore
{
    public Guid GeneratedId { get; set; } = Guid.Parse("9c7f2a31-84d5-4e6b-a1c2-3d4e5f607182");

    public Result<Guid> AddResult { get; set; }

    public Result<WatchedInstrumentConfiguration>? GetResult { get; set; }

    public Result<WatchedInstrumentPage>? SearchResult { get; set; }

    public Result<WatchedInstrumentSettingsSnapshot>? GetSettingsResult { get; set; }

    public Result<WatchedInstrumentSettingsSnapshot>? SaveSettingsResult { get; set; }

    public Exception? SearchException { get; set; }

    public Action? BeforeSearch { get; set; }

    public WatchedInstrumentRegistration? AddedRegistration { get; private set; }

    public Instant? RequestedAt { get; private set; }

    public (string? Query, int Page, int PageSize)? RequestedSearch { get; private set; }

    public WatchedInstrument? SavedInstrument { get; private set; }

    public PriceDisplayPreferences? SavedDisplay { get; private set; }

    public byte[]? ExpectedToken { get; private set; }

    public (IReadOnlyList<WatchedInstrumentDeleteItem> Items, Instant DeletedAt)? RequestedDelete { get; private set; }

    public Result<WatchedInstrumentBulkDeleteOutcome>? DeleteResult { get; set; }

    public StubWatchedInstrumentStore()
    {
        AddResult = GeneratedId;
    }

    public Task<Result<WatchedInstrumentRegistrationReceipt>> AddAsync(
        WatchedInstrumentRegistration registration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddedRegistration = registration;
        if (AddResult.IsFailure)
        {
            return Task.FromResult<Result<WatchedInstrumentRegistrationReceipt>>(
                AddResult.Error);
        }

        return Task.FromResult<Result<WatchedInstrumentRegistrationReceipt>>(
            new WatchedInstrumentRegistrationReceipt(
                AddResult.Value,
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
        RequestedAt = at;

        return Task.FromResult(GetResult ?? WatchedInstrumentErrors.ConfigurationNotFound);
    }

    public Task<Result<WatchedInstrumentPage>> SearchAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedSearch = (query, page, pageSize);
        BeforeSearch?.Invoke();

        if (SearchException is not null)
        {
            throw SearchException;
        }

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
            new WatchedInstrumentSettingsSnapshot(instrument, [9, 9, 9, 9, 9, 9, 9, 9])
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
}
