namespace TradingEngine.Application.Notifications;

public sealed record RecommendationEmail(
    string Subject,
    string PlainTextBody,
    string? HtmlBody = null,
    IReadOnlyList<string>? RecipientAddresses = null);
