namespace TradingEngine.Application.Notifications;

public enum EmailSendOutcome
{
    Accepted = 1,
    TransientFailure = 2,
    TerminalFailure = 3,
    Ambiguous = 4
}
