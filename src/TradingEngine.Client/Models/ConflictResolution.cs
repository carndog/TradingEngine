namespace TradingEngine.Client.Models;

public sealed class ConflictResolution<T>
{
    public T? Latest { get; private set; }

    public bool IsOpen => Latest is not null;

    public void Open(T latest)
    {
        Latest = latest;
    }

    public T Take()
    {
        T latest = Latest!;
        Latest = default;
        return latest;
    }
}
