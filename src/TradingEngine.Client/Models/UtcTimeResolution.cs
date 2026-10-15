namespace TradingEngine.Client.Models;

public readonly record struct UtcTimeResolution(UtcTimeResolutionKind Kind, DateTimeOffset? Value);
