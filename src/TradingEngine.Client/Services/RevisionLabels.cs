using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Services;

public static class RevisionLabels
{
    public static string ShortId(Guid id)
    {
        return id.ToString("N")[..8];
    }

    public static string DraftTitle(MonitoringRuleRevisionDto draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        string reason = string.IsNullOrWhiteSpace(draft.ChangeReason)
            ? "no change reason"
            : draft.ChangeReason.Trim();
        return $"Draft {ShortId(draft.Id)} · {reason}";
    }

    public static string DefinitionSummary(MonitoringRuleDefinitionDto? definition)
    {
        if (definition is null)
        {
            return "No definition";
        }

        int support = definition.SupportZones?.Count ?? 0;
        int resistance = definition.ResistanceZones?.Count ?? 0;
        return $"Price scale {definition.PriceScale} · {Plural(support, "support zone")} · {Plural(resistance, "resistance zone")}";
    }

    public static string RevisionTitle(MonitoringRuleRevisionDto revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        return revision.RevisionNumber is null
            ? $"Revision (unnumbered) {ShortId(revision.Id)}"
            : $"Revision {revision.RevisionNumber}";
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }
}
