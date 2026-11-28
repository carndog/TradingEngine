namespace TradingEngine.Application.WatchedInstruments.Verify;

public sealed record VerifyInstrument(
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    string? NativeQuoteCurrency = null);
