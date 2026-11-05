using System.Collections;
using System.Globalization;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.MarketDataSmoke;

public static class SmokeRunner
{
    public static async Task<int> RunAsync(
        IMarketDataProvider provider,
        MarketInstrumentIdentity identity,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Requesting {identity.Exchange}/{identity.Symbol}/" +
                $"{identity.QuoteCurrency} via the registered IMarketDataProvider."));

        Result<MarketQuote> result;
        try
        {
            result = await provider.GetCurrentQuoteAsync(identity, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await output.WriteLineAsync(
                $"FAILURE {exception.GetType().Name}: {exception.Message}");
            return 1;
        }

        if (result.IsFailure)
        {
            Error error = result.Error;
            await output.WriteLineAsync($"FAILURE {error.Code}: {error.Description}");
            if (error.Extensions is not null)
            {
                foreach (KeyValuePair<string, object?> extension in error.Extensions)
                {
                    await output.WriteLineAsync($"  {extension.Key}: {Format(extension.Value)}");
                }
            }

            return 1;
        }

        MarketQuote quote = result.Value;
        await output.WriteLineAsync("SUCCESS");
        await output.WriteLineAsync($"  Provider:              {quote.Provider}");
        await output.WriteLineAsync(
            $"  Instrument:            {quote.Instrument.Exchange}/" +
            $"{quote.Instrument.Symbol}/{quote.Instrument.QuoteCurrency}");
        await output.WriteLineAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  ProviderInstrumentId:  {quote.ProviderInstrumentId}"));
        await output.WriteLineAsync($"  Bid:                   {FormatDecimal(quote.Bid)}");
        await output.WriteLineAsync($"  Ask:                   {FormatDecimal(quote.Ask)}");
        await output.WriteLineAsync($"  Timeliness:            {quote.Timeliness}");
        await output.WriteLineAsync(
            $"  ProviderObservedAt:    {FormatInstant(quote.ProviderObservedAt)}");
        await output.WriteLineAsync($"  RetrievedAt:           {quote.RetrievedAt}");
        await output.WriteLineAsync(
            $"  ProviderRequestId:     {quote.ProviderRequestId ?? "(none)"}");
        return 0;
    }

    private static string Format(object? value)
    {
        return value switch
        {
            null => "(null)",
            string text => text,
            IEnumerable items => string.Join(", ", items.Cast<object?>()),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string FormatDecimal(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "(none)";
    }

    private static string FormatInstant(NodaTime.Instant? value)
    {
        return value?.ToString() ?? "(none)";
    }
}
