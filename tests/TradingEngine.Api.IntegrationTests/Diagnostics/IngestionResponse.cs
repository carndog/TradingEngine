using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class IngestionResponse : Response
{
    private readonly int _status;
    private Stream? _contentStream;

    public IngestionResponse(int status)
    {
        _status = status;
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"itemsReceived\":1,\"itemsAccepted\":1,\"errors\":[]}");
        _contentStream = new MemoryStream(payload);
    }

    public override int Status => _status;

    public override string ReasonPhrase => "OK";

    public override Stream? ContentStream
    {
        get => _contentStream;
        set => _contentStream = value;
    }

    public override string ClientRequestId { get; set; } = string.Empty;

    public override void Dispose()
    {
        _contentStream?.Dispose();
    }

    protected override bool ContainsHeader(string name)
    {
        return false;
    }

    protected override IEnumerable<HttpHeader> EnumerateHeaders()
    {
        yield break;
    }

    protected override bool TryGetHeader(
        string name,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }

    protected override bool TryGetHeaderValues(
        string name,
        [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }
}
