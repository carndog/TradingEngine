using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class MonitoringRuleRowConfiguration : IEntityTypeConfiguration<MonitoringRuleRow>
{
    public void Configure(EntityTypeBuilder<MonitoringRuleRow> builder)
    {
        builder.ToTable("MonitoringRules");

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator<SequentialGuidValueGenerator>();

        builder.Property(row => row.WatchedInstrumentId)
            .IsRequired();

        builder.Property(row => row.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.RowVersion)
            .IsRowVersion();

        builder.HasIndex(row => row.WatchedInstrumentId)
            .IsUnique()
            .HasDatabaseName("UX_MonitoringRules_WatchedInstrumentId");

        builder.HasOne(row => row.Instrument)
            .WithOne()
            .HasForeignKey<MonitoringRuleRow>(row => row.WatchedInstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
