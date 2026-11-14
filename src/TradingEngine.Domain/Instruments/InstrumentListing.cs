using NodaTime;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Instruments;

public sealed class InstrumentListing
{
    internal const int SymbolMaximumLength = 64;
    internal const int ExchangeMaximumLength = 20;
    internal const int ProviderMaximumLength = 32;
    internal const int ProviderFieldMaximumLength = 256;
    internal const int NativeUnitMinimumLength = 3;
    internal const int NativeUnitMaximumLength = 10;
    internal const int ConfirmedByMaximumLength = 128;

    private InstrumentListing(
        string exchange,
        string symbol,
        string provider,
        long providerInstrumentId,
        string? providerSymbol,
        string? providerExchangeName,
        string? displayName,
        string nativeQuoteCurrency,
        ListingUnitSource source,
        string confirmedBy,
        Instant confirmedAt)
    {
        Exchange = exchange;
        Symbol = symbol;
        Provider = provider;
        ProviderInstrumentId = providerInstrumentId;
        ProviderSymbol = providerSymbol;
        ProviderExchangeName = providerExchangeName;
        DisplayName = displayName;
        NativeQuoteCurrency = nativeQuoteCurrency;
        Source = source;
        ConfirmedBy = confirmedBy;
        ConfirmedAt = confirmedAt;
        LastChangedAt = confirmedAt;
    }

    public string Exchange { get; }

    public string Symbol { get; }

    public string Provider { get; }

    public long ProviderInstrumentId { get; private set; }

    public string? ProviderSymbol { get; private set; }

    public string? ProviderExchangeName { get; private set; }

    public string? DisplayName { get; private set; }

    public string NativeQuoteCurrency { get; }

    public ListingUnitSource Source { get; }

    public string ConfirmedBy { get; private set; }

    public Instant ConfirmedAt { get; }

    public Instant LastChangedAt { get; private set; }

    public static Result<InstrumentListing> Configure(
        string? exchange,
        string? symbol,
        string? provider,
        long providerInstrumentId,
        string? providerSymbol,
        string? providerExchangeName,
        string? displayName,
        string? nativeQuoteCurrency,
        ListingUnitSource source,
        string? confirmedBy,
        Instant confirmedAt)
    {
        Result<string> exchangeResult = NormalizeExchange(exchange);
        if (exchangeResult.IsFailure)
        {
            return exchangeResult.Error;
        }

        Result<string> symbolResult = NormalizeSymbol(symbol);
        if (symbolResult.IsFailure)
        {
            return symbolResult.Error;
        }

        Result<string> providerResult = NormalizeProvider(provider);
        if (providerResult.IsFailure)
        {
            return providerResult.Error;
        }

        if (providerInstrumentId <= 0)
        {
            return InstrumentListingErrors.ProviderInstrumentIdInvalid;
        }

        Result<string> nativeUnitResult = NormalizeNativeUnit(nativeQuoteCurrency);
        if (nativeUnitResult.IsFailure)
        {
            return nativeUnitResult.Error;
        }

        if (source is not ListingUnitSource.Administrator
            and not ListingUnitSource.ProviderMetadata)
        {
            return InstrumentListingErrors.UnitSourceUndefined;
        }

        Result<string> confirmedByResult = NormalizeConfirmedBy(confirmedBy);
        if (confirmedByResult.IsFailure)
        {
            return confirmedByResult.Error;
        }

        if (ProviderFieldExceedsMaximumLength(providerSymbol)
            || ProviderFieldExceedsMaximumLength(providerExchangeName)
            || ProviderFieldExceedsMaximumLength(displayName))
        {
            return InstrumentListingErrors.ProviderFieldExceedsMaximumLength;
        }

        return new InstrumentListing(
            exchangeResult.Value,
            symbolResult.Value,
            providerResult.Value,
            providerInstrumentId,
            providerSymbol,
            providerExchangeName,
            displayName,
            nativeUnitResult.Value,
            source,
            confirmedByResult.Value,
            confirmedAt);
    }

    public static Result<InstrumentListing> Restore(
        string? exchange,
        string? symbol,
        string? provider,
        long providerInstrumentId,
        string? providerSymbol,
        string? providerExchangeName,
        string? displayName,
        string? nativeQuoteCurrency,
        ListingUnitSource source,
        string? confirmedBy,
        Instant confirmedAt,
        Instant lastChangedAt)
    {
        Result<InstrumentListing> configured = Configure(
            exchange,
            symbol,
            provider,
            providerInstrumentId,
            providerSymbol,
            providerExchangeName,
            displayName,
            nativeQuoteCurrency,
            source,
            confirmedBy,
            confirmedAt);
        if (configured.IsFailure)
        {
            return configured;
        }

        if (lastChangedAt < confirmedAt)
        {
            return InstrumentListingErrors.LastChangedPrecedesConfirmed;
        }

        InstrumentListing listing = configured.Value;
        listing.LastChangedAt = lastChangedAt;

        return listing;
    }

    public static Result<string> NormalizeNativeUnit(string? nativeQuoteCurrency)
    {
        if (string.IsNullOrWhiteSpace(nativeQuoteCurrency))
        {
            return InstrumentListingErrors.NativeUnitRequired;
        }

        string normalized = nativeQuoteCurrency.Trim().ToUpperInvariant();

        if (normalized.Length is < NativeUnitMinimumLength or > NativeUnitMaximumLength
            || normalized.All(char.IsAsciiLetterOrDigit) is false)
        {
            return InstrumentListingErrors.NativeUnitInvalidFormat;
        }

        return normalized;
    }

    public Result RefreshProviderMetadata(
        long providerInstrumentId,
        string? providerSymbol,
        string? providerExchangeName,
        string? displayName,
        string? confirmedBy,
        Instant changedAt)
    {
        if (providerInstrumentId <= 0)
        {
            return InstrumentListingErrors.ProviderInstrumentIdInvalid;
        }

        if (ProviderFieldExceedsMaximumLength(providerSymbol)
            || ProviderFieldExceedsMaximumLength(providerExchangeName)
            || ProviderFieldExceedsMaximumLength(displayName))
        {
            return InstrumentListingErrors.ProviderFieldExceedsMaximumLength;
        }

        Result<string> confirmedByResult = NormalizeConfirmedBy(confirmedBy);
        if (confirmedByResult.IsFailure)
        {
            return confirmedByResult.Error;
        }

        if (changedAt < LastChangedAt)
        {
            return InstrumentListingErrors.LastChangedPrecedesConfirmed;
        }

        ProviderInstrumentId = providerInstrumentId;
        ProviderSymbol = providerSymbol;
        ProviderExchangeName = providerExchangeName;
        DisplayName = displayName;
        ConfirmedBy = confirmedByResult.Value;
        LastChangedAt = changedAt;

        return Result.Success();
    }

    private static bool ProviderFieldExceedsMaximumLength(string? value)
    {
        return value is not null && value.Length > ProviderFieldMaximumLength;
    }

    private static Result<string> NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return InstrumentListingErrors.ProviderRequired;
        }

        string normalized = provider.Trim();

        if (normalized.Length > ProviderMaximumLength)
        {
            return InstrumentListingErrors.ProviderExceedsMaximumLength;
        }

        return normalized;
    }

    private static Result<string> NormalizeConfirmedBy(string? confirmedBy)
    {
        if (string.IsNullOrWhiteSpace(confirmedBy))
        {
            return InstrumentListingErrors.ConfirmedByRequired;
        }

        string normalized = confirmedBy.Trim();

        if (normalized.Length > ConfirmedByMaximumLength)
        {
            return InstrumentListingErrors.ConfirmedByExceedsMaximumLength;
        }

        return normalized;
    }

    private static Result<string> NormalizeSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return InstrumentListingErrors.SymbolRequired;
        }

        string normalized = symbol.Trim().ToUpperInvariant();

        if (normalized.Length > SymbolMaximumLength)
        {
            return InstrumentListingErrors.SymbolExceedsMaximumLength;
        }

        if (normalized.Any(char.IsWhiteSpace))
        {
            return InstrumentListingErrors.SymbolContainsWhitespace;
        }

        return normalized;
    }

    private static Result<string> NormalizeExchange(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            return InstrumentListingErrors.ExchangeRequired;
        }

        string normalized = exchange.Trim().ToUpperInvariant();

        if (normalized.Length > ExchangeMaximumLength)
        {
            return InstrumentListingErrors.ExchangeExceedsMaximumLength;
        }

        if (normalized.All(IsAllowedExchangeCharacter) is false)
        {
            return InstrumentListingErrors.ExchangeInvalidCharacters;
        }

        return normalized;
    }

    private static bool IsAllowedExchangeCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_';
    }
}
