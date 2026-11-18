using System.Buffers.Binary;

namespace TradingEngine.Application.Signals;

public sealed record SignalTrackingState(Guid SignalId, bool Archived, long Version, DateTimeOffset? ChangedAt)
{
    public byte[] ConcurrencyToken
    {
        get
        {
            byte[] token = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(token, Version);
            return token;
        }
    }
}
