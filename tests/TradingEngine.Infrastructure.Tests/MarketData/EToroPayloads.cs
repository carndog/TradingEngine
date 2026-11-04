namespace TradingEngine.Infrastructure.Tests.MarketData;

internal static class EToroPayloads
{
    public static string SearchBody(
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
        return $"{{\"page\":1,\"pageSize\":25,\"totalItems\":{items.Length},\"items\":[{entries}]}}";
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
