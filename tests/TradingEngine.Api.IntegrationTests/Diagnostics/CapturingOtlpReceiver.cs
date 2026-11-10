using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class CapturingOtlpReceiver : IDisposable
{
    internal const string TracesPath = "/v1/traces";
    internal const string LogsPath = "/v1/logs";
    internal const string MetricsPath = "/v1/metrics";

    private readonly HttpListener _listener = new();
    private readonly object _gate = new();
    private readonly List<CapturedOtlpRequest> _requests = [];
    private readonly Dictionary<string, TaskCompletionSource> _pathsReceived = new(StringComparer.Ordinal);
    private readonly Task _receiveLoop;

    public CapturingOtlpReceiver()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{FindFreePort()}/");
        _listener.Prefixes.Add(BaseAddress.ToString());
        _listener.Start();
        _receiveLoop = Task.Run(ReceiveAsync);
    }

    public Uri BaseAddress { get; }

    public IReadOnlyList<CapturedOtlpRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public Task WaitForPathAsync(string path, TimeSpan timeout)
    {
        return PathReceived(path).Task.WaitAsync(timeout);
    }

    public string PayloadText(string path)
    {
        IEnumerable<string> payloads = Requests
            .Where(request => request.Path == path)
            .Select(request => Encoding.UTF8.GetString(request.Body));

        return string.Join("\n", payloads);
    }

    public string AllPayloadText()
    {
        return string.Join("\n", Requests.Select(request => Encoding.UTF8.GetString(request.Body)));
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        _receiveLoop.Wait(TimeSpan.FromSeconds(5));
    }

    private async Task ReceiveAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            await CaptureAsync(context);
        }
    }

    private async Task CaptureAsync(HttpListenerContext context)
    {
        using MemoryStream body = new();
        await context.Request.InputStream.CopyToAsync(body);

        string path = context.Request.Url?.AbsolutePath ?? string.Empty;
        lock (_gate)
        {
            _requests.Add(new CapturedOtlpRequest(path, context.Request.ContentType, body.ToArray()));
        }

        PathReceived(path).TrySetResult();

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/x-protobuf";
        context.Response.ContentLength64 = 0;
        context.Response.Close();
    }

    private TaskCompletionSource PathReceived(string path)
    {
        lock (_gate)
        {
            if (_pathsReceived.TryGetValue(path, out TaskCompletionSource? received) is false)
            {
                received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pathsReceived[path] = received;
            }

            return received;
        }
    }

    private static int FindFreePort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }
}
