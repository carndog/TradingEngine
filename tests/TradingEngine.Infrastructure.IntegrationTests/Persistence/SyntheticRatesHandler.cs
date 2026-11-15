using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

internal sealed class SyntheticRatesHandler : HttpMessageHandler
{
    public Func<Task>? BeforeResponse { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/api/v2/market-data/rates"));
        if (BeforeResponse is not null)
        {
            await BeforeResponse();
        }

        long[] ids = request.RequestUri.Query.Split('=')[1].Split(',')
            .Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        string payload = JsonSerializer.Serialize(new
        {
            results = ids.Select(id => new
            {
                instrumentId = id,
                bid = 12.345m,
                ask = 12.346m,
                date = "2026-01-05T12:00:00Z",
                quoteType = "realtime"
            })
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }
}
