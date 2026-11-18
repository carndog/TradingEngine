using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportAttemptRowConfiguration : IEntityTypeConfiguration<WeeklyReportAttemptRow>
{
    public void Configure(EntityTypeBuilder<WeeklyReportAttemptRow> builder)
    {
        builder.ToTable("WeeklyReportAttempts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).UseIdentityColumn();
        builder.Property(row => row.OccurredAt).HasColumnType("datetime2(7)").HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
        builder.Property(row => row.Outcome).HasMaxLength(32);
        builder.Property(row => row.ProviderOperationId).HasMaxLength(128);
        builder.Property(row => row.ProviderStatus).HasMaxLength(64);
        builder.Property(row => row.ErrorKind).HasMaxLength(64);
        builder.HasIndex(row => new { row.ReportId, row.Id });
        builder.HasOne<WeeklyReportRow>().WithMany().HasForeignKey(row => row.ReportId).OnDelete(DeleteBehavior.Restrict);
    }
}
