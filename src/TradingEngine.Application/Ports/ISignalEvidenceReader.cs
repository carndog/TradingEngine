using TradingEngine.Application.Signals;

namespace TradingEngine.Application.Ports;

public interface ISignalEvidenceReader
{
    Task<IReadOnlyList<SignalEvidence>> ReadAsync(SignalQuery query, CancellationToken cancellationToken);
}
