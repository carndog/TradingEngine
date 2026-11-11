using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class NotificationIntentRowConfiguration
    : IEntityTypeConfiguration<NotificationIntentRow>
{
    public void Configure(EntityTypeBuilder<NotificationIntentRow> builder)
    {
        builder.ToTable("NotificationIntents");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator<SequentialGuidValueGenerator>();

        builder.Property(row => row.WatchedInstrumentId)
            .IsRequired();

        builder.Property(row => row.ScheduledFor)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.PreviousScheduledFor)
            .HasColumnType("datetime2(7)")
            .HasConversion(
                InstantConverters.ToUtcDateTimeNullable,
                InstantConverters.FromUtcDateTimeNullable);

        builder.Property(row => row.MonitoringRuleRevisionId)
            .IsRequired();

        builder.Property(row => row.Reason)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("nvarchar(32)");

        builder.Property(row => row.PriceBasis)
            .IsRequired()
            .HasMaxLength(8)
            .HasColumnType("nvarchar(8)");

        builder.Property(row => row.QuoteCurrency)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnType("nvarchar(10)");

        builder.Property(row => row.CurrentBid)
            .HasPrecision(38, 18);

        builder.Property(row => row.CurrentAsk)
            .HasPrecision(38, 18);

        builder.Property(row => row.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasColumnType("nvarchar(16)");

        builder.Property(row => row.ProviderOperationId)
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.ClaimToken);

        builder.Property(row => row.ClaimedUntil)
            .HasColumnType("datetime2(7)")
            .HasConversion(
                InstantConverters.ToUtcDateTimeNullable,
                InstantConverters.FromUtcDateTimeNullable);

        builder.Property(row => row.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.HasIndex(row => new { row.WatchedInstrumentId, row.ScheduledFor })
            .IsUnique()
            .HasDatabaseName("UX_NotificationIntents_WatchedInstrumentId_ScheduledFor");

        builder.HasIndex(row => new { row.Status, row.ScheduledFor })
            .HasDatabaseName("IX_NotificationIntents_Status_ScheduledFor");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationIntents_Status",
            "[Status] IN ('Pending', 'Ambiguous', 'Sent', 'Failed')"));

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationIntents_PriceBasis",
            "[PriceBasis] IN ('Bid', 'Ask')"));

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationIntents_Reason",
            "[Reason] IN ('ConditionsTriggered')"));

        builder.HasOne(row => row.Instrument)
            .WithMany()
            .HasForeignKey(row => row.WatchedInstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
