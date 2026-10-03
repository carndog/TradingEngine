using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class MonitoringRuleRevisionRow
{
    public Guid Id { get; set; }

    public Guid MonitoringRuleId { get; set; }

    public int? RevisionNumber { get; set; }

    public Instant CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string? ChangeReason { get; set; }

    public string DefinitionXml { get; set; } = string.Empty;

    public Instant? EffectiveFrom { get; set; }

    public Instant? EffectiveTo { get; set; }

    public Instant? ProposedFrom { get; set; }

    public Instant? ProposedTo { get; set; }

    public MonitoringRuleRow Rule { get; set; } = null!;
}
