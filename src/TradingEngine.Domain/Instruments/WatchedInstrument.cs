using NodaTime;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Instruments;

public sealed class WatchedInstrument
{
    private const int SymbolMaximumLength = 64;
    private const int ExchangeMaximumLength = 20;
    private const int QuoteCurrencyMinimumLength = 3;
    private const int QuoteCurrencyMaximumLength = 10;
    private const int MinimumSamplingIntervalSeconds = 1;
    private const int MaximumSamplingIntervalSeconds = 3600;

    private WatchedInstrument(
        Guid id,
        string symbol,
        string exchange,
        string quoteCurrency,
        int samplingIntervalSeconds,
        Instant createdAt)
    {
        Id = id;
        Symbol = symbol;
        Exchange = exchange;
        QuoteCurrency = quoteCurrency;
        SamplingIntervalSeconds = samplingIntervalSeconds;
        MonitoringState = MonitoringState.Configured;
        CreatedAt = createdAt;
        LastChangedAt = createdAt;
    }

    public Guid Id { get; }

    public string Symbol { get; }

    public string Exchange { get; }

    public string QuoteCurrency { get; }

    public MonitoringState MonitoringState { get; private set; }

    public int SamplingIntervalSeconds { get; private set; }

    public Instant CreatedAt { get; }

    public Instant LastChangedAt { get; private set; }

    public static Result<WatchedInstrument> Create(
        Guid id,
        string? symbol,
        string? exchange,
        string? quoteCurrency,
        int samplingIntervalSeconds,
        Instant createdAt)
    {
        if (id == Guid.Empty)
        {
            return WatchedInstrumentErrors.IdRequired;
        }

        Result<WatchedInstrumentFields> fields = Validate(
            symbol,
            exchange,
            quoteCurrency,
            samplingIntervalSeconds);
        if (fields.IsFailure)
        {
            return fields.Error;
        }

        WatchedInstrumentFields value = fields.Value;
        return new WatchedInstrument(
            id,
            value.Symbol,
            value.Exchange,
            value.QuoteCurrency,
            value.SamplingIntervalSeconds,
            createdAt);
    }

    public static Result<WatchedInstrumentFields> Validate(
        string? symbol,
        string? exchange,
        string? quoteCurrency,
        int samplingIntervalSeconds)
    {
        Result<string> symbolResult = NormalizeSymbol(symbol);
        if (symbolResult.IsFailure)
        {
            return symbolResult.Error;
        }

        Result<string> exchangeResult = NormalizeExchange(exchange);
        if (exchangeResult.IsFailure)
        {
            return exchangeResult.Error;
        }

        Result<string> quoteCurrencyResult = NormalizeQuoteCurrency(quoteCurrency);
        if (quoteCurrencyResult.IsFailure)
        {
            return quoteCurrencyResult.Error;
        }

        if (IsValidSamplingInterval(samplingIntervalSeconds) is false)
        {
            return WatchedInstrumentErrors.SamplingIntervalOutOfRange;
        }

        return new WatchedInstrumentFields(
            symbolResult.Value,
            exchangeResult.Value,
            quoteCurrencyResult.Value,
            samplingIntervalSeconds);
    }

    public static Result<WatchedInstrument> Restore(
        Guid id,
        string? symbol,
        string? exchange,
        string? quoteCurrency,
        MonitoringState monitoringState,
        int samplingIntervalSeconds,
        Instant createdAt,
        Instant lastChangedAt)
    {
        Result<WatchedInstrument> created = Create(
            id,
            symbol,
            exchange,
            quoteCurrency,
            samplingIntervalSeconds,
            createdAt);
        if (created.IsFailure)
        {
            return created;
        }

        if (Enum.IsDefined(monitoringState) is false)
        {
            return WatchedInstrumentErrors.MonitoringStateUndefined;
        }

        if (lastChangedAt < createdAt)
        {
            return WatchedInstrumentErrors.LastChangedPrecedesCreated;
        }

        WatchedInstrument instrument = created.Value;
        instrument.MonitoringState = monitoringState;
        instrument.LastChangedAt = lastChangedAt;

        return instrument;
    }

    public Result StartMonitoring(int samplingIntervalSeconds, Instant changedAt)
    {
        Error? failure = ValidateChangeTimestamp(changedAt);
        if (failure is not null)
        {
            return failure;
        }

        if (IsValidSamplingInterval(samplingIntervalSeconds) is false)
        {
            return WatchedInstrumentErrors.SamplingIntervalOutOfRange;
        }

        if (MonitoringState == MonitoringState.Monitored)
        {
            return WatchedInstrumentErrors.AlreadyMonitored;
        }

        SamplingIntervalSeconds = samplingIntervalSeconds;
        MonitoringState = MonitoringState.Monitored;
        LastChangedAt = changedAt;

        return Result.Success();
    }

    public Result StopMonitoring(Instant changedAt)
    {
        Error? failure = ValidateChangeTimestamp(changedAt);
        if (failure is not null)
        {
            return failure;
        }

        if (MonitoringState == MonitoringState.Configured)
        {
            return WatchedInstrumentErrors.NotMonitored;
        }

        MonitoringState = MonitoringState.Configured;
        LastChangedAt = changedAt;

        return Result.Success();
    }

    public Result ChangeSamplingInterval(int samplingIntervalSeconds, Instant changedAt)
    {
        Error? failure = ValidateChangeTimestamp(changedAt);
        if (failure is not null)
        {
            return failure;
        }

        if (IsValidSamplingInterval(samplingIntervalSeconds) is false)
        {
            return WatchedInstrumentErrors.SamplingIntervalOutOfRange;
        }

        if (SamplingIntervalSeconds == samplingIntervalSeconds)
        {
            return WatchedInstrumentErrors.SamplingIntervalUnchanged;
        }

        SamplingIntervalSeconds = samplingIntervalSeconds;
        LastChangedAt = changedAt;

        return Result.Success();
    }

    private static Result<string> NormalizeSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return WatchedInstrumentErrors.SymbolRequired;
        }

        string normalized = symbol.Trim().ToUpperInvariant();

        if (normalized.Length > SymbolMaximumLength)
        {
            return WatchedInstrumentErrors.SymbolExceedsMaximumLength;
        }

        if (normalized.Any(char.IsWhiteSpace))
        {
            return WatchedInstrumentErrors.SymbolContainsWhitespace;
        }

        return normalized;
    }

    private static Result<string> NormalizeExchange(string? exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            return WatchedInstrumentErrors.ExchangeRequired;
        }

        string normalized = exchange.Trim().ToUpperInvariant();

        if (normalized.Length > ExchangeMaximumLength)
        {
            return WatchedInstrumentErrors.ExchangeExceedsMaximumLength;
        }

        if (normalized.All(IsAllowedExchangeCharacter) is false)
        {
            return WatchedInstrumentErrors.ExchangeInvalidCharacters;
        }

        return normalized;
    }

    private static Result<string> NormalizeQuoteCurrency(string? quoteCurrency)
    {
        if (string.IsNullOrWhiteSpace(quoteCurrency))
        {
            return WatchedInstrumentErrors.QuoteCurrencyRequired;
        }

        string normalized = quoteCurrency.Trim().ToUpperInvariant();

        if (normalized.Length is < QuoteCurrencyMinimumLength or > QuoteCurrencyMaximumLength
            || normalized.All(char.IsAsciiLetterOrDigit) is false)
        {
            return WatchedInstrumentErrors.QuoteCurrencyInvalidFormat;
        }

        return normalized;
    }

    private static bool IsAllowedExchangeCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_';
    }

    private static bool IsValidSamplingInterval(int samplingIntervalSeconds)
    {
        return samplingIntervalSeconds is >= MinimumSamplingIntervalSeconds
            and <= MaximumSamplingIntervalSeconds;
    }

    private Error? ValidateChangeTimestamp(Instant changedAt)
    {
        if (changedAt < LastChangedAt)
        {
            return WatchedInstrumentErrors.ChangePrecedesLatestChange;
        }

        return null;
    }
}
