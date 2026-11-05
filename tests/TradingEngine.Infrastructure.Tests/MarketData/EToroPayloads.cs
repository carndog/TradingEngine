namespace TradingEngine.Infrastructure.Tests.MarketData;

internal static class EToroPayloads
{
    public static string SearchBody(
        params (long Id, string Symbol, int ExchangeId, string Exchange)[] items)
    {
        return SearchBody(1, items.Length, items);
    }

    public static string SearchBody(
        int page,
        int totalItems,
        params (long Id, string Symbol, int ExchangeId, string Exchange)[] items)
    {
        string entries = string.Join(
            ",",
            items.Select(item => "{" +
                $"\"instrumentId\":{item.Id}," +
                $"\"internalSymbolFull\":\"{item.Symbol}\"," +
                $"\"internalExchangeId\":{item.ExchangeId}," +
                $"\"internalExchangeName\":\"{item.Exchange}\"," +
                $"\"exchangeID\":{item.ExchangeId}" +
                "}"));
        return $"{{\"page\":{page},\"pageSize\":25,\"totalItems\":{totalItems}," +
            $"\"items\":[{entries}]}}";
    }

    public static (long Id, string Symbol, int ExchangeId, string Exchange)[] SearchFillers(
        long startId,
        int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => (startId + index, $"FILL{index}", 99, "Filler Venue"))
            .ToArray();
    }

    public static string RatesBody(
        params (long Id, string Bid, string Ask, string Date, string QuoteType)[] rates)
    {
        string entries = string.Join(
            ",",
            rates.Select(rate => "{" +
                $"\"instrumentId\":{rate.Id}," +
                $"\"bid\":{rate.Bid}," +
                $"\"ask\":{rate.Ask}," +
                $"\"date\":\"{rate.Date}\"," +
                $"\"quoteType\":\"{rate.QuoteType}\"" +
                "}"));
        return $"{{\"results\":[{entries}]}}";
    }
}
