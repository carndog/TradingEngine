using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

internal sealed class CapturingWatchedInstrumentStore : IWatchedInstrumentStore
{
    private readonly Result _addResult;

    public CapturingWatchedInstrumentStore()
        : this(Result.Success())
    {
    }

    public CapturingWatchedInstrumentStore(Result addResult)
    {
        _addResult = addResult;
    }

    public WatchedInstrumentConfiguration? AddedConfiguration { get; private set; }

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
        throw new NotSupportedException();
    }
}
