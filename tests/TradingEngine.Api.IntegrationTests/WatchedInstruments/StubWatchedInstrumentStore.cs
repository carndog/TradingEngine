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

    public WatchedInstrumentRegistration? AddedRegistration { get; private set; }

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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(GetResult ?? WatchedInstrumentErrors.ConfigurationNotFound);
    }
}
