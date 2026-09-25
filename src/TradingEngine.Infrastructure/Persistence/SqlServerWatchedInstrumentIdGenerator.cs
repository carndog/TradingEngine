using Microsoft.EntityFrameworkCore.ValueGeneration;
using TradingEngine.Application.Ports;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerWatchedInstrumentIdGenerator : IWatchedInstrumentIdGenerator
{
    private readonly SequentialGuidValueGenerator _generator = new();

    public Guid NewId()
    {
        return _generator.Next(null!);
    }
}
