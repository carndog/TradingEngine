using NodaTime;
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

    public WatchedInstrumentRegistration? AddedRegistration { get; private set; }

    public Instant? RequestedAt { get; private set; }

    public (string? Query, int Page, int PageSize)? RequestedSearch { get; private set; }

    public WatchedInstrument? SavedInstrument { get; private set; }

    public byte[]? ExpectedToken { get; private set; }

    public StubWatchedInstrumentStore()
    {
        AddResult = GeneratedId;
    }

    public Task<Result<Guid>> AddAsync(
        WatchedInstrumentRegistration registration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddedRegistration = registration;
        return Task.FromResult(AddResult);
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
        byte[] expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedInstrument = instrument;
        ExpectedToken = expectedConcurrencyToken;

        if (SaveSettingsResult is not null)
        {
            return Task.FromResult(SaveSettingsResult);
        }

        return Task.FromResult<Result<WatchedInstrumentSettingsSnapshot>>(
            new WatchedInstrumentSettingsSnapshot(instrument, [9, 9, 9, 9, 9, 9, 9, 9]));
    }
}
