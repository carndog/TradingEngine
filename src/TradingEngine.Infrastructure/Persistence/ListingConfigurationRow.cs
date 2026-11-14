using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class ListingConfigurationRow
{
    public Guid Id { get; set; }

    public string Exchange { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public long ProviderInstrumentId { get; set; }

    public string? ProviderSymbol { get; set; }

    public string? ProviderExchangeName { get; set; }

    public string? DisplayName { get; set; }

    public string NativeQuoteCurrency { get; set; } = string.Empty;

    public string UnitSource { get; set; } = string.Empty;

    public string ConfirmedBy { get; set; } = string.Empty;

    public Instant ConfirmedAt { get; set; }

    public Instant LastChangedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
