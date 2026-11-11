using TradingEngine.Application.Notifications;

namespace TradingEngine.Application.Tests.Notifications;

internal sealed record RecordedSend(RecommendationEmail Email, Guid OperationId);
