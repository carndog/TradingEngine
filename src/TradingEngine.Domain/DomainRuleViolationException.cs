namespace TradingEngine.Domain;

public sealed class DomainRuleViolationException : InvalidOperationException
{
    public DomainRuleViolationException(string message)
        : base(message)
    {
    }
}
