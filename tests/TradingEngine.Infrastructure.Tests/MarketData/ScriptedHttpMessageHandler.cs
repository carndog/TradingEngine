using System.Net;

namespace TradingEngine.Infrastructure.Tests.MarketData;

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();

    public List<CapturedRequest> Requests { get; } = [];

    public void Enqueue(HttpResponseMessage response)
    {
        _script.Enqueue((_, _) => Task.FromResult(response));
    }

    public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _script.Enqueue(responder);
    }

    public void EnqueueResponse(HttpStatusCode statusCode, string body)
    {
        Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(
            request.RequestUri,
            request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase)));

        if (_script.Count == 0)
        {
            throw new InvalidOperationException(
                $"The scripted handler received an unexpected request to '{request.RequestUri}'.");
        }

        return _script.Dequeue()(request, cancellationToken);
    }
}
