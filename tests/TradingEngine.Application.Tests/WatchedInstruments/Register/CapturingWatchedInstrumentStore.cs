using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

internal sealed class CapturingWatchedInstrumentStore : IWatchedInstrumentStore
{
    private readonly Result _addResult;
    private readonly Result<WatchedInstrumentConfiguration>? _getResult;

    public CapturingWatchedInstrumentStore()
        : this(Result.Success(), null)
    {
    }

    public CapturingWatchedInstrumentStore(Result addResult)
        : this(addResult, null)
    {
    }

    public CapturingWatchedInstrumentStore(
        Result addResult,
        Result<WatchedInstrumentConfiguration>? getResult)
    {
        _addResult = addResult;
        _getResult = getResult;
    }

    public WatchedInstrumentConfiguration? AddedConfiguration { get; private set; }

    public Guid? RequestedInstrumentId { get; private set; }

    public Task<Result> AddAsync(
        WatchedInstrumentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddedConfiguration = configuration;
        return Task.FromResult(_addResult);
    }

    public Task<Result<WatchedInstrumentConfiguration>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedInstrumentId = instrumentId;

        return Task.FromResult(_getResult ?? WatchedInstrumentErrors.ConfigurationNotFound);
    }
}
