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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedInstrumentId = instrumentId;

        return Task.FromResult(_getResult ?? WatchedInstrumentErrors.ConfigurationNotFound);
    }
}
