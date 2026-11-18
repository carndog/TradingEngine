using System.Buffers.Binary;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Signals;

public sealed class ChangeSignalTrackingHandler(ISignalTrackingStore store, IClock clock)
{
    public static Error InvalidToken => Error.Validation("signals.invalid_token", "Supply a valid signal tracking ETag.");

    public static Error TokenRequired => Error.PreconditionRequired("signals.token_required", "If-Match is required to change signal tracking.");

    public static Error StaleToken => Error.PreconditionFailed("signals.stale_token", "Signal tracking changed; reload its current ETag.");

    public Task<Result<SignalTrackingState>> HandleAsync(Guid signalId, bool archived, byte[]? expectedToken,
        string actor, CancellationToken cancellationToken)
    {
        if (expectedToken is null)
        {
            return Task.FromResult<Result<SignalTrackingState>>(TokenRequired);
        }

        if (expectedToken.Length != 8 || BinaryPrimitives.ReadInt64BigEndian(expectedToken) < 0)
        {
            return Task.FromResult<Result<SignalTrackingState>>(InvalidToken);
        }

        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128)
        {
            return Task.FromResult<Result<SignalTrackingState>>(Error.Validation("signals.actor_invalid", "A valid audit actor is required."));
        }

        return store.SetAsync(signalId, archived, BinaryPrimitives.ReadInt64BigEndian(expectedToken), actor,
            clock.GetCurrentInstant(), cancellationToken);
    }
}
