namespace TradingEngine.Application.Notifications;

internal sealed record AssetEmailCooldownOverride(Guid InstrumentId, string Period,
    decimal? FromInclusive = null, decimal? ToExclusive = null);
