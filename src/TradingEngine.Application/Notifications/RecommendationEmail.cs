namespace TradingEngine.Application.Notifications;

public sealed record RecommendationEmail(
    string Subject,
    string PlainTextBody);
