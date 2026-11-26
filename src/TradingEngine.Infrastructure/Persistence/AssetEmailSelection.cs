using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed record AssetEmailSelection(decimal Price, Instant? AlertedAt);
