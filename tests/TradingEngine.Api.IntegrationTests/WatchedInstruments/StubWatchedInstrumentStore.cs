using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

internal sealed class StubWatchedInstrumentStore : IWatchedInstrumentStore
{
    public Result AddResult { get; set; } = Result.Success();

    public Result<WatchedInstrumentConfiguration>? GetResult { get; set; }

    public WatchedInstrumentConfiguration? AddedConfiguration { get; private set; }

    public Task<Result> AddAsync(
        WatchedInstrumentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddedConfiguration = configuration;
        return Task.FromResult(AddResult);
    }

    public Task<Result<WatchedInstrumentConfiguration>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(GetResult ?? WatchedInstrumentErrors.ConfigurationNotFound);
    }
}
