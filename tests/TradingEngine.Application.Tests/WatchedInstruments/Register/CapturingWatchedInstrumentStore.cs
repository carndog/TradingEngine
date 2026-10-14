using NodaTime;
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

    public byte[]? ExpectedToken { get; private set; }

    public (string? Query, int Page, int PageSize)? RequestedSearch { get; private set; }

    public Task<Result<Guid>> AddAsync(
        WatchedInstrumentRegistration registration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddedRegistration = registration;
        return Task.FromResult(_addResult);
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
            new WatchedInstrumentSettingsSnapshot(instrument, [7, 7, 7, 7, 7, 7, 7, 7]));
    }
}
