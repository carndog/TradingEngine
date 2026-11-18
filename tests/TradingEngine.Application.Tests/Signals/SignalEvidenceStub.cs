using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;

namespace TradingEngine.Application.Tests.Signals;

internal sealed class SignalEvidenceStub : ISignalEvidenceReader
{
    public IReadOnlyList<SignalEvidence> Items { get; set; } = [];

    public int Calls { get; private set; }

    public Task<IReadOnlyList<SignalEvidence>> ReadAsync(SignalQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(Items);
    }
}
