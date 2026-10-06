namespace TradingEngine.Contracts.MonitoringRules;

public sealed record RevisionPeriodDto(
    string? EffectiveFrom,
    string? EffectiveTo);
