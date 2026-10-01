namespace TradingEngine.Domain.Tests.Revisions;

public sealed record SyntheticLimitDefinition
{
    public SyntheticLimitDefinition(int limit, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        Limit = limit;
        Tags = tags.ToArray();
    }

    public int Limit { get; }

    public IReadOnlyList<string> Tags { get; }
}
