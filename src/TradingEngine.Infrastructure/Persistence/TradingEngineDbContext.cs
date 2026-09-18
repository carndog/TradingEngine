using Microsoft.EntityFrameworkCore;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class TradingEngineDbContext : DbContext
{
    public TradingEngineDbContext(DbContextOptions<TradingEngineDbContext> options)
        : base(options)
    {
    }

    internal DbSet<WatchedInstrumentRow> WatchedInstruments
    {
        get { return Set<WatchedInstrumentRow>(); }
    }

    internal DbSet<ChartAnalysisDefinitionRow> ChartAnalysisDefinitions
    {
        get { return Set<ChartAnalysisDefinitionRow>(); }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingEngineDbContext).Assembly);
    }
}
