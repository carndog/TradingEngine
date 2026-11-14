namespace TradingEngine.Application.Listings;

public sealed record ConfirmListingUnit(
    Guid InstrumentId,
    string? NativeQuoteCurrency,
    byte[]? ExpectedConcurrencyToken,
    string ConfirmedBy);
