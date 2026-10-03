using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class MonitoringRuleRevisionRowConfiguration
    : IEntityTypeConfiguration<MonitoringRuleRevisionRow>
{
    public void Configure(EntityTypeBuilder<MonitoringRuleRevisionRow> builder)
    {
        builder.ToTable("MonitoringRuleRevisions", table =>
        {
            table.HasCheckConstraint(
                "CK_MonitoringRuleRevisions_Period",
                "([EffectiveFrom] IS NULL AND [EffectiveTo] IS NULL) "
                    + "OR ([EffectiveFrom] IS NOT NULL AND ([EffectiveTo] IS NULL "
                    + "OR [EffectiveTo] > [EffectiveFrom]))");
            table.HasCheckConstraint(
                "CK_MonitoringRuleRevisions_Numbering",
                "([EffectiveFrom] IS NULL AND [RevisionNumber] IS NULL) "
                    + "OR ([EffectiveFrom] IS NOT NULL AND [RevisionNumber] IS NOT NULL "
                    + "AND [RevisionNumber] > 0)");
        });

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .ValueGeneratedNever();

        builder.Property(row => row.MonitoringRuleId)
            .IsRequired();

        builder.Property(row => row.RevisionNumber);

        builder.Property(row => row.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(7)")
            .HasConversion(InstantConverters.ToUtcDateTime, InstantConverters.FromUtcDateTime);

        builder.Property(row => row.CreatedBy)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(row => row.ChangeReason)
            .HasMaxLength(512);

        builder.Property(row => row.DefinitionXml)
            .HasColumnType("xml")
            .IsRequired();

        builder.Property(row => row.EffectiveFrom)
            .HasColumnType("datetime2(7)")
            .HasConversion(
                InstantConverters.ToUtcDateTimeNullable,
                InstantConverters.FromUtcDateTimeNullable);

        builder.Property(row => row.EffectiveTo)
            .HasColumnType("datetime2(7)")
            .HasConversion(
                InstantConverters.ToUtcDateTimeNullable,
                InstantConverters.FromUtcDateTimeNullable);

        builder.Property(row => row.ProposedFrom)
            .HasColumnType("datetime2(7)")
            .HasConversion(
                InstantConverters.ToUtcDateTimeNullable,
                InstantConverters.FromUtcDateTimeNullable);

        builder.Property(row => row.ProposedTo)
            .HasColumnType("datetime2(7)")
            .HasConversion(
                InstantConverters.ToUtcDateTimeNullable,
                InstantConverters.FromUtcDateTimeNullable);

        builder.HasIndex(row => new { row.MonitoringRuleId, row.EffectiveFrom })
            .IsUnique()
            .HasFilter("[EffectiveFrom] IS NOT NULL")
            .HasDatabaseName("UX_MonitoringRuleRevisions_EffectiveFrom");

        builder.HasIndex(row => new { row.MonitoringRuleId, row.RevisionNumber })
            .IsUnique()
            .HasFilter("[RevisionNumber] IS NOT NULL")
            .HasDatabaseName("UX_MonitoringRuleRevisions_RevisionNumber");

        builder.HasOne(row => row.Rule)
            .WithMany(rule => rule.Revisions)
            .HasForeignKey(row => row.MonitoringRuleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
