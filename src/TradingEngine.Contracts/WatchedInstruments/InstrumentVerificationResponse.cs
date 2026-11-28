namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record InstrumentVerificationResponse(
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    string ResolutionState,
    string? ResolutionDetailCode,
    string? Provider,
    string? ProviderSymbol,
    string? ProviderExchangeName,
    int? ProviderExchangeId,
    string? DisplayName,
    string? NativeQuoteCurrency,
    bool ReadyForMonitoring,
    string? ReadinessDetailCode,
    string SessionStatus,
    string? SessionDetailCode,
    VerificationQuoteDto? Quote,
    string? QuoteDetailCode);
