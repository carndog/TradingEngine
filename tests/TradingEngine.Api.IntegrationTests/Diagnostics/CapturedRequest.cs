namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed record CapturedRequest(string Uri, string? Body, string? ContentEncoding);
