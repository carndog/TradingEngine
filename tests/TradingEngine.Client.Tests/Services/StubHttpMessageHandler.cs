using System.Net;

namespace TradingEngine.Client.Tests.Services;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _handlers = new();

    public int RequestCount { get; private set; }

    public IReadOnlyList<HttpMethod> Methods => _methods;

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Fallback { get; set; }

    private readonly List<HttpMethod> _methods = [];

    public void Enqueue(HttpResponseMessage response)
    {
        _handlers.Enqueue((_, _) => Task.FromResult(response));
    }

    public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handlers.Enqueue(handler);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        _methods.Add(request.Method);

        if (_handlers.Count > 0)
        {
            return _handlers.Dequeue()(request, cancellationToken);
        }

        if (Fallback is not null)
        {
            return Fallback(request, cancellationToken);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        });
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage Html(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html")
        };
    }
}
