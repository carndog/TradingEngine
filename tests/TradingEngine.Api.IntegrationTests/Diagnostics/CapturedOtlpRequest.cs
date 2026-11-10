namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed record CapturedOtlpRequest(string Path, string? ContentType, byte[] Body);
