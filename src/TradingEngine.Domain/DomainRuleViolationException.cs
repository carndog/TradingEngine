namespace TradingEngine.Domain;

public sealed class DomainRuleViolationException : InvalidOperationException
{
    public DomainRuleViolationException(Enum rule, string message)
        : base(message)
    {
        Rule = rule;
    }

    public Enum Rule { get; }
}
