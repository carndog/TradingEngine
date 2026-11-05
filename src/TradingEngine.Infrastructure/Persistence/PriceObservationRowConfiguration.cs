using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class PriceObservationRowConfiguration : IEntityTypeConfiguration<PriceObservationRow>
{
    public void Configure(EntityTypeBuilder<PriceObservationRow> builder)
    {
        builder.ToTable("PriceObservations");

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

        builder.Property(row => row.Provider)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("nvarchar(32)");

        builder.Property(row => row.ProviderInstrumentId)
            .IsRequired();

        builder.Property(row => row.Bid)
            .HasPrecision(38, 18);

        builder.Property(row => row.Ask)
            .HasPrecision(38, 18);

        builder.Property(row => row.QuoteCurrency)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnType("nvarchar(10)");

        builder.Property(row => row.ProviderObservedAt)
            .HasColumnType("datetime2(7)")
            .HasConversion(
                InstantConverters.ToUtcDateTimeNullable,
                InstantConverters.FromUtcDateTimeNullable);

        builder.Property(row => row.RetrievedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.Timeliness)
            .IsRequired()
            .HasMaxLength(16)
            .HasColumnType("nvarchar(16)");

        builder.Property(row => row.ProviderRequestId)
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.InvocationId)
            .IsRequired();

        builder.HasIndex(row => new { row.WatchedInstrumentId, row.ScheduledFor })
            .IsUnique()
            .HasDatabaseName("UX_PriceObservations_WatchedInstrumentId_ScheduledFor");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_PriceObservations_Timeliness",
            "[Timeliness] IN ('Unknown', 'Realtime', 'Delayed')"));

        builder.HasOne(row => row.Instrument)
            .WithMany()
            .HasForeignKey(row => row.WatchedInstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
