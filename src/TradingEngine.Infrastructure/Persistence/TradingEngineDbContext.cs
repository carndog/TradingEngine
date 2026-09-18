using Microsoft.EntityFrameworkCore;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class TradingEngineDbContext : DbContext
{
    public TradingEngineDbContext(DbContextOptions<TradingEngineDbContext> options)
        : base(options)
    {
    }

    internal DbSet<WatchedInstrumentRow> WatchedInstruments => Set<WatchedInstrumentRow>();

    internal DbSet<ChartAnalysisDefinitionRow> ChartAnalysisDefinitions => Set<ChartAnalysisDefinitionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingEngineDbContext).Assembly);
    }
}
