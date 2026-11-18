using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportRowConfiguration : IEntityTypeConfiguration<WeeklyReportRow>
{
    public void Configure(EntityTypeBuilder<WeeklyReportRow> builder)
    {
        builder.ToTable("WeeklyReports", table =>
        {
            table.HasCheckConstraint("CK_WeeklyReports_GenerationStatus", "[GenerationStatus] IN ('Generating', 'Ready', 'Empty')");
            table.HasCheckConstraint("CK_WeeklyReports_DeliveryStatus", "[DeliveryStatus] IN ('Skipped', 'Pending', 'Submitted', 'Ambiguous', 'Sent', 'Failed')");
            table.HasCheckConstraint("CK_WeeklyReports_Period", "[PeriodStart] < [Cutoff] AND [Cutoff] < [NextCutoff]");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.SubscriptionKey).HasMaxLength(160);
        builder.Property(row => row.GenerationStatus).HasMaxLength(16);
        builder.Property(row => row.DeliveryStatus).HasMaxLength(16);
        builder.Property(row => row.Subject).HasMaxLength(256);
        builder.Property(row => row.ProviderOperationId).HasMaxLength(128);
        builder.Property(row => row.GenerationError).HasMaxLength(64);
        builder.Property(row => row.PeriodStart).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
        builder.Property(row => row.Cutoff).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
        builder.Property(row => row.NextCutoff).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
        builder.Property(row => row.CreatedAt).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
        builder.Property(row => row.UpdatedAt).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
        builder.Property(row => row.GenerationClaimedUntil).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTimeNullable, InstantConverters.FromUtcDateTimeNullable);
        builder.Property(row => row.DeliveryClaimedUntil).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTimeNullable, InstantConverters.FromUtcDateTimeNullable);
        builder.Property(row => row.FirstSkippedCutoff).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTimeNullable, InstantConverters.FromUtcDateTimeNullable);
        builder.HasIndex(row => new { row.SubscriptionKey, row.Cutoff }).IsUnique();
        builder.HasIndex(row => new { row.SubscriptionKey, row.GenerationStatus, row.DeliveryStatus, row.UpdatedAt });
        builder.HasOne<WeeklyReportScheduleRow>().WithMany().HasForeignKey(row => row.SubscriptionKey).OnDelete(DeleteBehavior.Restrict);
    }
}
