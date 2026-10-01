using Azure.Core;
using Azure.Core.Pipeline;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class CapturingTransport : HttpClientTransport
{
    private readonly object _gate = new();
    private readonly List<CapturedRequest> _requests = [];
    private readonly TaskCompletionSource _firstRequestCaptured = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstRequestCaptured => _firstRequestCaptured.Task;

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public override void Process(HttpMessage message)
    {
        Capture(message);
        message.Response = new IngestionResponse(200);
    }

    public override ValueTask ProcessAsync(HttpMessage message)
    {
        Capture(message);
        message.Response = new IngestionResponse(200);
        return ValueTask.CompletedTask;
    }

    private void Capture(HttpMessage message)
    {
        string? body = null;
        if (message.Request.Content is not null)
        {
            using MemoryStream stream = new();
            message.Request.Content.WriteTo(stream, CancellationToken.None);
            body = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        string? contentEncoding = null;
        if (message.Request.Headers.TryGetValue("Content-Encoding", out string? encoding))
        {
            contentEncoding = encoding;
        }

        lock (_gate)
        {
            _requests.Add(new CapturedRequest(
                message.Request.Uri.ToUri().AbsoluteUri,
                body,
                contentEncoding));
        }

        _firstRequestCaptured.TrySetResult();
    }
}
