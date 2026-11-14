using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

internal sealed class StubMarketDataProvider : IMarketDataProvider
{
    public Func<MarketListingQuery, CancellationToken, Task<Result<MarketListing>>>?
        ListingResponder { get; set; }

    public Func<MarketInstrumentIdentity, CancellationToken, Task<Result<MarketQuote>>>?
        QuoteResponder { get; set; }

    public List<MarketListingQuery> ListingQueries { get; } = [];

    public List<MarketInstrumentIdentity> QuoteRequests { get; } = [];

    public Task<Result<MarketListing>> ResolveListingAsync(
        MarketListingQuery query,
        CancellationToken cancellationToken)
    {
        ListingQueries.Add(query);

        if (ListingResponder is not null)
        {
            return ListingResponder(query, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<Result<MarketListing>>(new MarketListing(
            "eToro",
            4242,
            query.Symbol,
            "Test Exchange",
            5,
            $"{query.Symbol} Synthetic",
            null));
    }

    public Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        QuoteRequests.Add(instrument);

        if (QuoteResponder is not null)
        {
            return QuoteResponder(instrument, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Instant now = SystemClock.Instance.GetCurrentInstant();
        return Task.FromResult<Result<MarketQuote>>(new MarketQuote(
            instrument,
            "eToro",
            4242,
            123.45m,
            124.01m,
            now,
            now,
            MarketQuoteTimeliness.Realtime,
            $"synthetic-request-{QuoteRequests.Count}"));
    }
}
