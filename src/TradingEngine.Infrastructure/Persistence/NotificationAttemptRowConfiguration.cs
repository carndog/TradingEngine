using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class NotificationAttemptRowConfiguration
    : IEntityTypeConfiguration<NotificationAttemptRow>
{
    public void Configure(EntityTypeBuilder<NotificationAttemptRow> builder)
    {
        builder.ToTable("NotificationAttempts");

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator<SequentialGuidValueGenerator>();

        builder.Property(row => row.NotificationIntentId)
            .IsRequired();

        builder.Property(row => row.AttemptedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.Outcome)
            .IsRequired()
            .HasMaxLength(24)
            .HasColumnType("nvarchar(24)");

        builder.Property(row => row.ProviderOperationId)
            .HasMaxLength(64)
            .HasColumnType("nvarchar(64)");

        builder.Property(row => row.ProviderStatus)
            .HasMaxLength(16)
            .HasColumnType("nvarchar(16)");

        builder.Property(row => row.ErrorKind)
            .HasMaxLength(32)
            .HasColumnType("nvarchar(32)");

        builder.Property(row => row.RecordedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.HasIndex(row => row.NotificationIntentId)
            .HasDatabaseName("IX_NotificationAttempts_NotificationIntentId");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_NotificationAttempts_Outcome",
            "[Outcome] IN ('Submitted', 'Failed', 'Ambiguous', 'ResolvedSucceeded', " +
            "'ResolvedRejected', 'Unresolved', 'Suppressed')"));

        builder.HasOne(row => row.Intent)
            .WithMany()
            .HasForeignKey(row => row.NotificationIntentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
