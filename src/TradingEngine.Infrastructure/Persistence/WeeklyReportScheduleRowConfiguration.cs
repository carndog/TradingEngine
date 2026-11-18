using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportScheduleRowConfiguration : IEntityTypeConfiguration<WeeklyReportScheduleRow>
{
    public void Configure(EntityTypeBuilder<WeeklyReportScheduleRow> builder)
    {
        builder.ToTable("WeeklyReportSchedules");
        builder.HasKey(row => row.SubscriptionKey);
        builder.Property(row => row.SubscriptionKey).HasMaxLength(160);
        builder.Property(row => row.Fingerprint).HasMaxLength(160);
        builder.Property(row => row.NextCutoff).HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
    }
}
