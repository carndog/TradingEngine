namespace TradingEngine.Contracts.MonitoringRules;

public sealed record RevisionPeriodDto(
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);
