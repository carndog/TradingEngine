using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class ListingConfigurationRowConfiguration
    : IEntityTypeConfiguration<ListingConfigurationRow>
{
    public void Configure(EntityTypeBuilder<ListingConfigurationRow> builder)
    {
        builder.ToTable("ListingConfigurations");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator<SequentialGuidValueGenerator>();

        builder.Property(row => row.Exchange)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("nvarchar(20)");

        builder.Property(row => row.Symbol)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.Provider)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("nvarchar(32)");

        builder.Property(row => row.ProviderInstrumentId)
            .IsRequired();

        builder.Property(row => row.ProviderSymbol)
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.ProviderExchangeName)
            .HasMaxLength(128)
            .HasColumnType("nvarchar(128)");

        builder.Property(row => row.DisplayName)
            .HasMaxLength(256)
            .HasColumnType("nvarchar(256)");

        builder.Property(row => row.NativeQuoteCurrency)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnType("nvarchar(10)");

        builder.Property(row => row.UnitSource)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("nvarchar(20)");

        builder.Property(row => row.ConfirmedBy)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnType("nvarchar(128)");

        builder.Property(row => row.ConfirmedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.LastChangedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.RowVersion)
            .IsRowVersion();

        builder.HasIndex(row => new { row.Exchange, row.Symbol })
            .IsUnique()
            .HasDatabaseName("UX_ListingConfigurations_Exchange_Symbol");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ListingConfigurations_UnitSource",
            "[UnitSource] IN ('Administrator', 'ProviderMetadata')"));

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ListingConfigurations_ChangeTimestamps",
            "[LastChangedAt] >= [ConfirmedAt]"));
    }
}
