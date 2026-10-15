namespace TradingEngine.Client.Services;

public sealed class SessionState
{
    public event Action? Invalidated;

    public void Invalidate()
    {
        Invalidated?.Invoke();
    }
}
