using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Infrastructure.Persistence;

internal static class MonitoringRuleRowMapping
{
    public static MonitoringRuleRevisionRow ToRow(
        Guid monitoringRuleId,
        Revision<ChartAnalysisDefinition> revision,
        ChartAnalysisDefinitionXmlSerializer serializer)
    {
        return new MonitoringRuleRevisionRow
        {
            Id = revision.Id,
            MonitoringRuleId = monitoringRuleId,
            RevisionNumber = revision.RevisionNumber,
            CreatedAt = revision.CreatedAt,
            CreatedBy = revision.CreatedBy,
            ChangeReason = revision.ChangeReason,
            DefinitionXml = serializer.Serialize(revision.Definition),
            EffectiveFrom = revision.EffectivePeriod?.EffectiveFrom,
            EffectiveTo = revision.EffectivePeriod?.EffectiveTo,
            ProposedFrom = revision.Proposal?.EffectiveFrom,
            ProposedTo = revision.Proposal?.EffectiveTo
        };
    }

    public static MonitoringRule Restore(
        MonitoringRuleRow row,
        ChartAnalysisDefinitionXmlSerializer serializer)
    {
        List<RestoredRevision<ChartAnalysisDefinition>> revisions = new(row.Revisions.Count);

        foreach (MonitoringRuleRevisionRow revision in row.Revisions)
        {
            ChartAnalysisDefinition definition = serializer.Deserialize(revision.DefinitionXml);
            revisions.Add(new RestoredRevision<ChartAnalysisDefinition>(
                revision.Id,
                definition,
                revision.CreatedAt,
                revision.CreatedBy,
                revision.ChangeReason,
                revision.RevisionNumber,
                revision.EffectiveFrom,
                revision.EffectiveTo,
                revision.ProposedFrom,
                revision.ProposedTo));
        }

        Result<MonitoringRule> restored = MonitoringRule.Restore(
            row.Id,
            row.WatchedInstrumentId,
            row.CreatedAt,
            revisions);

        if (restored.IsFailure)
        {
            throw new InvalidDataException(
                $"The persisted monitoring rule '{row.Id}' is invalid " +
                $"({restored.Error.Code}): {restored.Error.Description}");
        }

        return restored.Value;
    }

    public static void ValidateInstants(MonitoringRule rule)
    {
        SqlInstant.RequireSupported(rule.CreatedAt, nameof(rule));

        foreach (Revision<ChartAnalysisDefinition> revision in rule.Revisions.Concat(rule.Drafts))
        {
            SqlInstant.RequireSupported(revision.CreatedAt, nameof(rule));
            SqlInstant.RequireSupported(revision.EffectivePeriod?.EffectiveFrom, nameof(rule));
            SqlInstant.RequireSupported(revision.EffectivePeriod?.EffectiveTo, nameof(rule));
            SqlInstant.RequireSupported(revision.Proposal?.EffectiveFrom, nameof(rule));
            SqlInstant.RequireSupported(revision.Proposal?.EffectiveTo, nameof(rule));
        }
    }
}
