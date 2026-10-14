using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Models;

public sealed class PendingAction
{
    public PendingAction(PendingActionKind kind, MonitoringRuleRevisionDto revision, string capturedToken)
    {
        Kind = kind;
        Revision = revision;
        CapturedToken = capturedToken;
    }

    public PendingActionKind Kind { get; }

    public MonitoringRuleRevisionDto Revision { get; }

    public string CapturedToken { get; set; }

    public UtcTimeField? From { get; set; }

    public UtcTimeField? To { get; set; }

    public bool HasInput => From?.Modified == true || To?.Modified == true;
}
