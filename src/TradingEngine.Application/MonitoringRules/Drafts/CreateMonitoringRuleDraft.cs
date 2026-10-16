using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Application.MonitoringRules.Drafts;

public sealed record CreateMonitoringRuleDraft(
    Guid InstrumentId,
    Guid DraftId,
    ChartAnalysisDefinition Definition,
    string? ChangeReason,
    RevisionProposal? Proposal,
    byte[]? ExpectedConcurrencyToken,
    string? CreatedBy);
