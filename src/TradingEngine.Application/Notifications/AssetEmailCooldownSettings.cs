namespace TradingEngine.Application.Notifications;

internal sealed record AssetEmailCooldownSettings(string DefaultPeriod, AssetEmailCooldownOverride[]? Overrides);
