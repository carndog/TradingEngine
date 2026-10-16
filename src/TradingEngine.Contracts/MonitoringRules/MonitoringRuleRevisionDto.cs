namespace TradingEngine.Contracts.MonitoringRules;

public sealed record MonitoringRuleRevisionDto(
    Guid Id,
    string Kind,
    int? RevisionNumber,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    bool CreatedByCaller,
    string? ChangeReason,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset? ProposedEffectiveFrom,
    DateTimeOffset? ProposedEffectiveTo,
    MonitoringRuleDefinitionDto Definition);
