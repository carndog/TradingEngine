namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record ListingDto(
    string? ResolutionState,
    string? ResolutionDetailCode,
    string? Provider,
    long? ProviderInstrumentId,
    string? ProviderSymbol,
    string? ProviderExchangeName,
    string? DisplayName,
    string UnitState,
    string? NativeQuoteCurrency,
    string? UnitSource,
    bool DeploymentConfigured,
    string? ConcurrencyToken);
