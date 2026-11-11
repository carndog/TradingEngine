namespace TradingEngine.Infrastructure.Persistence;

internal sealed class NotificationIntentMatchRow
{
    public Guid Id { get; set; }

    public Guid NotificationIntentId { get; set; }

    public string ZoneId { get; set; } = string.Empty;

    public string Condition { get; set; } = string.Empty;

    public string ActionId { get; set; } = string.Empty;

    public decimal Lower { get; set; }

    public decimal Level { get; set; }

    public decimal Upper { get; set; }

    public decimal PreviousPrice { get; set; }

    public decimal CurrentPrice { get; set; }

    public NotificationIntentRow Intent { get; set; } = null!;
}
