namespace TradingEngine.Infrastructure.Notifications;

public sealed class EmailNotificationOptions
{
    public const string SectionName = "Notifications:Email";

    public string ConnectionString { get; set; } = string.Empty;

    public string SenderAddress { get; set; } = string.Empty;

    public string[] RecipientAddresses { get; set; } = [];
}
