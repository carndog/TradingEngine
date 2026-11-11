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

    internal DbSet<MonitoringRuleRow> MonitoringRules
    {
        get { return Set<MonitoringRuleRow>(); }
    }

    internal DbSet<MonitoringRuleRevisionRow> MonitoringRuleRevisions
    {
        get { return Set<MonitoringRuleRevisionRow>(); }
    }

    internal DbSet<PriceObservationRow> PriceObservations
    {
        get { return Set<PriceObservationRow>(); }
    }

    internal DbSet<PriceCollectionAttemptRow> PriceCollectionAttempts
    {
        get { return Set<PriceCollectionAttemptRow>(); }
    }

    internal DbSet<EvaluationCheckpointRow> RecommendationCheckpoints
    {
        get { return Set<EvaluationCheckpointRow>(); }
    }

    internal DbSet<NotificationIntentRow> NotificationIntents
    {
        get { return Set<NotificationIntentRow>(); }
    }

    internal DbSet<NotificationIntentMatchRow> NotificationIntentMatches
    {
        get { return Set<NotificationIntentMatchRow>(); }
    }

    internal DbSet<NotificationAttemptRow> NotificationAttempts
    {
        get { return Set<NotificationAttemptRow>(); }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingEngineDbContext).Assembly);
    }
}
