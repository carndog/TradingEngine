using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class SignalTrackingAuditRowConfiguration : IEntityTypeConfiguration<SignalTrackingAuditRow>
{
    public void Configure(EntityTypeBuilder<SignalTrackingAuditRow> builder)
    {
        builder.ToTable("SignalTrackingAudit");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).UseIdentityColumn();
        builder.Property(row => row.ChangedAt).HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);
        builder.Property(row => row.ChangedBy).IsRequired().HasMaxLength(128);
        builder.HasIndex(row => new { row.SignalId, row.ChangedAt, row.Id });
        builder.HasOne<NotificationIntentMatchRow>().WithMany().HasForeignKey(row => row.SignalId).OnDelete(DeleteBehavior.Restrict);
    }
}
