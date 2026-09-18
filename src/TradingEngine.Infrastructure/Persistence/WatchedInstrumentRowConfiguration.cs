using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WatchedInstrumentRowConfiguration : IEntityTypeConfiguration<WatchedInstrumentRow>
{
    public void Configure(EntityTypeBuilder<WatchedInstrumentRow> builder)
    {
        builder.ToTable("WatchedInstruments");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();

        builder.Property(row => row.Symbol)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.Exchange)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("nvarchar(20)");

        builder.Property(row => row.QuoteCurrency)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnType("nvarchar(10)");

        builder.Property(row => row.MonitoringState)
            .IsRequired()
            .HasMaxLength(16)
            .HasColumnType("nvarchar(16)");

        builder.Property(row => row.SamplingIntervalSeconds)
            .IsRequired();

        builder.Property(row => row.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.LastChangedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.HasIndex(row => new { row.Exchange, row.Symbol, row.QuoteCurrency })
            .IsUnique()
            .HasDatabaseName("UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_WatchedInstruments_SamplingIntervalSeconds",
            "[SamplingIntervalSeconds] >= 1 AND [SamplingIntervalSeconds] <= 3600"));

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_WatchedInstruments_MonitoringState",
            "[MonitoringState] IN ('Configured', 'Monitored')"));

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_WatchedInstruments_ChangeTimestamps",
            "[LastChangedAt] >= [CreatedAt]"));

        builder.HasOne(row => row.ChartAnalysisDefinition)
            .WithOne(row => row.Instrument)
            .HasForeignKey<ChartAnalysisDefinitionRow>(row => row.WatchedInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
