using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportItemRowConfiguration : IEntityTypeConfiguration<WeeklyReportItemRow>
{
    public void Configure(EntityTypeBuilder<WeeklyReportItemRow> builder)
    {
        builder.ToTable("WeeklyReportItems");
        builder.HasKey(row => new { row.ReportId, row.Position });
        builder.HasIndex(row => new { row.ReportId, row.SignalId }).IsUnique();
        builder.HasOne<WeeklyReportRow>().WithMany().HasForeignKey(row => row.ReportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NotificationIntentMatchRow>().WithMany().HasForeignKey(row => row.SignalId).OnDelete(DeleteBehavior.Restrict);
    }
}
