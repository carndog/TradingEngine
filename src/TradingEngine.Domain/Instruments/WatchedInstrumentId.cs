namespace TradingEngine.Domain.Instruments;

public sealed record WatchedInstrumentId
{
    private WatchedInstrumentId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static WatchedInstrumentId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                WatchedInstrumentIdRule.Empty,
                "A watched-instrument identifier cannot be empty.");
        }

        return new WatchedInstrumentId(value);
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
