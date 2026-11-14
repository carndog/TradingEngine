using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.Listings;

internal sealed class StubNativeQuoteUnitMap : INativeQuoteUnitMap
{
    public Result<NativeQuoteUnitResolution> Result { get; set; } =
        new NativeQuoteUnitResolution("USD", null, "USD", false);

    public List<(string Exchange, string Symbol)> Calls { get; } = [];

    public Task<Result<NativeQuoteUnitResolution>> ResolveAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken)
    {
        Calls.Add((exchange, symbol));
        return Task.FromResult(Result);
    }

    public static StubNativeQuoteUnitMap Configured(string currency)
    {
        return new StubNativeQuoteUnitMap
        {
            Result = new NativeQuoteUnitResolution(currency, null, currency, false)
        };
    }

    public static StubNativeQuoteUnitMap Unconfigured()
    {
        return new StubNativeQuoteUnitMap
        {
            Result = new NativeQuoteUnitResolution(null, null, null, false)
        };
    }
}
