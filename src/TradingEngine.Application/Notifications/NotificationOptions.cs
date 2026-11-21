namespace TradingEngine.Application.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; }

    public int PollIntervalSeconds { get; set; } = 30;

    public int MaxIntentsPerRun { get; set; } = 50;

    public int ClaimLeaseSeconds { get; set; } = 120;

    public int MaxDispatchAttempts { get; set; } = 5;
}
