using System.Security.Cryptography;
using TradingEngine.Application.Ports;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SequentialWatchedInstrumentIdGenerator : IWatchedInstrumentIdGenerator
{
    private int _sequence;

    public Guid NewId()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(16);
        long milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 6; i++)
        {
            bytes[15 - i] = (byte)(milliseconds >> (8 * i));
        }

        int sequence = Interlocked.Increment(ref _sequence);
        bytes[8] = (byte)(sequence >> 8);
        bytes[9] = (byte)sequence;

        return new Guid(bytes);
    }
}
