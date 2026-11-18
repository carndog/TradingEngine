namespace TradingEngine.Application.Notifications;

public sealed record EmailDeliveryWork(Guid Id, Func<RecommendationEmail> Render);
