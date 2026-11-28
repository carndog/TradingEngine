namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record VerifyInstrumentRequest(
    string? Symbol,
    string? Exchange,
    string? QuoteCurrency,
    string? NativeQuoteCurrency = null);
