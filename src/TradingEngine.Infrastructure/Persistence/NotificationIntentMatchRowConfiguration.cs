using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class NotificationIntentMatchRowConfiguration
    : IEntityTypeConfiguration<NotificationIntentMatchRow>
{
    public void Configure(EntityTypeBuilder<NotificationIntentMatchRow> builder)
    {
        builder.ToTable("NotificationIntentMatches");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator<SequentialGuidValueGenerator>();

        builder.Property(row => row.NotificationIntentId)
            .IsRequired();

        builder.Property(row => row.ZoneId)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.Condition)
            .IsRequired()
            .HasMaxLength(16)
            .HasColumnType("nvarchar(16)");

        builder.Property(row => row.ActionId)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.Lower)
            .HasPrecision(38, 18);

        builder.Property(row => row.Level)
            .HasPrecision(38, 18);

        builder.Property(row => row.Upper)
            .HasPrecision(38, 18);

        builder.Property(row => row.PreviousPrice)
            .HasPrecision(38, 18);

        builder.Property(row => row.CurrentPrice)
            .HasPrecision(38, 18);

        builder.HasIndex(row => row.NotificationIntentId)
            .HasDatabaseName("IX_NotificationIntentMatches_NotificationIntentId");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationIntentMatches_Condition",
            "[Condition] IN ('BuyZone', 'SupportLoss', 'Breakout')"));

        builder.HasOne(row => row.Intent)
            .WithMany(intent => intent.Matches)
            .HasForeignKey(row => row.NotificationIntentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
