using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class PriceCollectionAttemptRowConfiguration
    : IEntityTypeConfiguration<PriceCollectionAttemptRow>
{
    public void Configure(EntityTypeBuilder<PriceCollectionAttemptRow> builder)
    {
        builder.ToTable("PriceCollectionAttempts");

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

        builder.Property(row => row.Outcome)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("nvarchar(32)");

        builder.Property(row => row.ErrorCode)
            .HasMaxLength(128)
            .HasColumnType("nvarchar(128)");

        builder.Property(row => row.Provider)
            .HasMaxLength(32)
            .HasColumnType("nvarchar(32)");

        builder.Property(row => row.ProviderRequestId)
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.InvocationId)
            .IsRequired();

        builder.Property(row => row.AttemptedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.RecordedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.HasIndex(row => new { row.WatchedInstrumentId, row.ScheduledFor })
            .HasDatabaseName("IX_PriceCollectionAttempts_WatchedInstrumentId_ScheduledFor");

        builder.HasIndex(row => row.InvocationId)
            .HasDatabaseName("IX_PriceCollectionAttempts_InvocationId");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_PriceCollectionAttempts_Outcome",
            "[Outcome] IN ('Observed', 'SkippedMarketClosed', 'SkippedDuplicate', " +
            "'Throttled', 'Failed', 'PriceUnavailable')"));

        builder.HasOne(row => row.Instrument)
            .WithMany()
            .HasForeignKey(row => row.WatchedInstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
