using NodaTime;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.Listings;

public sealed record ListingConfigurationUpsert(
    string Exchange,
    string Symbol,
    string Provider,
    long ProviderInstrumentId,
    string? ProviderSymbol,
    string? ProviderExchangeName,
    string? DisplayName,
    string NativeQuoteCurrency,
    ListingUnitSource Source,
    string ConfirmedBy,
    Instant ConfirmedAt);
