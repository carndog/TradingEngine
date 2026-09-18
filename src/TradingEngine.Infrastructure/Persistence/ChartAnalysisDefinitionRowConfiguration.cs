using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class ChartAnalysisDefinitionRowConfiguration
    : IEntityTypeConfiguration<ChartAnalysisDefinitionRow>
{
    public void Configure(EntityTypeBuilder<ChartAnalysisDefinitionRow> builder)
    {
        builder.ToTable("ChartAnalysisDefinitions");

        builder.HasKey(row => row.WatchedInstrumentId);
        builder.Property(row => row.WatchedInstrumentId).ValueGeneratedNever();

        builder.Property(row => row.DefinitionXml)
            .IsRequired()
            .HasColumnType("xml");
    }
}
