using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class EvaluationCheckpointRowConfiguration
    : IEntityTypeConfiguration<EvaluationCheckpointRow>
{
    public void Configure(EntityTypeBuilder<EvaluationCheckpointRow> builder)
    {
        builder.ToTable("RecommendationCheckpoints");

        builder.HasKey(row => row.WatchedInstrumentId);

        builder.Property(row => row.LastProcessedScheduledFor)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.EstablishedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.HasOne(row => row.Instrument)
            .WithMany()
            .HasForeignKey(row => row.WatchedInstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
