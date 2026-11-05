using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class StubMarketDataProvider : IMarketDataProvider
{
    public List<MarketInstrumentIdentity> Requests { get; } = [];

    public Func<MarketInstrumentIdentity, CancellationToken, Task<Result<MarketQuote>>>?
        Responder { get; set; }

    public decimal? Bid { get; set; } = 123.456789012345678m;

    public decimal? Ask { get; set; } = 124.000000000000001m;

    public Instant? ProviderObservedAt { get; set; }

    public Instant RetrievedAt { get; set; }

    public Task<Result<MarketQuote>> GetCurrentQuoteAsync(
        MarketInstrumentIdentity instrument,
        CancellationToken cancellationToken)
    {
        Requests.Add(instrument);

        if (Responder is not null)
        {
            return Responder(instrument, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<Result<MarketQuote>>(new MarketQuote(
            instrument,
            "eToro",
            4200 + Requests.Count,
            Bid,
            Ask,
            ProviderObservedAt,
            RetrievedAt,
            MarketQuoteTimeliness.Realtime,
            $"synthetic-request-{Requests.Count}"));
    }
}
